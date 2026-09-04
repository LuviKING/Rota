package app.rota.estudos;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import org.json.JSONTokener;

import java.text.ParseException;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Locale;
import java.util.Set;

public final class StudyPlanImporter {
    public static final int MAX_CODE_CHARS = 256_000;
    public static final int MAX_SESSIONS = 1_000;

    private StudyPlanImporter() {}

    public static PlanPackage parse(String raw) {
        if (raw == null || raw.trim().isEmpty()) {
            throw new IllegalArgumentException("Cole um StudyPlan Code antes de importar.");
        }
        if (raw.length() > MAX_CODE_CHARS) {
            throw new IllegalArgumentException("O plano excede o limite de 256 mil caracteres.");
        }

        try {
            JSONTokener tokener = new JSONTokener(raw);
            Object rootValue = tokener.nextValue();
            if (!(rootValue instanceof JSONObject)) {
                throw new IllegalArgumentException("O StudyPlan precisa ser um objeto JSON.");
            }
            if (tokener.nextClean() != 0) {
                throw new IllegalArgumentException("Há conteúdo extra depois do objeto JSON.");
            }
            JSONObject root = (JSONObject) rootValue;
            rejectUnknownKeys(root, "raiz", "format", "format_version", "plan", "objective", "sessions");

            if (!"studyplan".equals(requiredText(root, "format", 20))) {
                throw new IllegalArgumentException("Campo format deve ser \"studyplan\".");
            }
            String formatVersion = requiredText(root, "format_version", 10);
            if (!"0.2".equals(formatVersion) && !"0.1".equals(formatVersion)) {
                throw new IllegalArgumentException("Versão do StudyPlan não suportada: " + formatVersion);
            }

            JSONObject plan = requireObject(root, "plan");
            rejectUnknownKeys(plan, "plan", "id", "revision", "title");
            String planId = requiredText(plan, "id", 80);
            int revision = requiredInteger(plan, "revision");
            if (revision < 1) {
                throw new IllegalArgumentException("plan.revision deve ser maior que zero.");
            }
            String title = optionalNonBlankText(plan, "title", "Meu plano", 120);

            JSONObject objective = optionalObject(root, "objective");
            if (objective != null) {
                rejectUnknownKeys(objective, "objective", "name", "date");
            }
            String objectiveName = objective == null
                    ? title
                    : optionalNonBlankText(objective, "name", title, 120);
            String objectiveDate = objective == null
                    ? ""
                    : optionalText(objective, "date", "", 10);
            if (!objectiveDate.isEmpty()) {
                requireIsoDate(objectiveDate, "objective.date");
            }

            JSONArray items = requireArray(root, "sessions");
            if (items.length() == 0) {
                throw new IllegalArgumentException("O plano não possui sessões.");
            }
            if (items.length() > MAX_SESSIONS) {
                throw new IllegalArgumentException("O plano excede o limite de 1000 sessões.");
            }

            List<SessionItem> sessions = new ArrayList<>();
            Set<String> ids = new HashSet<>();
            for (int i = 0; i < items.length(); i++) {
                Object rawItem = items.get(i);
                if (!(rawItem instanceof JSONObject)) {
                    throw new IllegalArgumentException("sessions[" + i + "] precisa ser um objeto.");
                }
                JSONObject item = (JSONObject) rawItem;
                rejectUnknownKeys(
                        item,
                        "sessions[" + i + "]",
                        "id", "date", "subject", "topic", "minutes", "target", "kind", "review_label"
                );

                String id = requiredText(item, "id", 100);
                if (id.contains(ScheduleRules.RUNTIME_REVIEW_ID_MARKER)) {
                    throw new IllegalArgumentException(
                            "O ID " + id + " usa o marcador reservado " +
                                    ScheduleRules.RUNTIME_REVIEW_ID_MARKER + "."
                    );
                }
                if (!ids.add(id)) {
                    throw new IllegalArgumentException("ID de sessão duplicado: " + id);
                }

                String date = requiredText(item, "date", 10);
                requireIsoDate(date, "sessions[" + i + "].date");
                if (!objectiveDate.isEmpty() && date.compareTo(objectiveDate) > 0) {
                    throw new IllegalArgumentException(
                            "A sessão " + id + " está depois da data do objetivo (" + objectiveDate + ")."
                    );
                }

                String subject = requiredText(item, "subject", 80);
                String topic = requiredText(item, "topic", 160);
                int minutes = requiredInteger(item, "minutes");
                if (minutes < 10 || minutes > 360) {
                    throw new IllegalArgumentException("minutes deve ficar entre 10 e 360.");
                }

                String target = "0.2".equals(formatVersion)
                        ? requiredText(item, "target", 180)
                        : optionalText(item, "target", "", 180);
                String kind = optionalText(item, "kind", "study", 20);
                if (!"study".equals(kind) && !"review".equals(kind) && !"assessment".equals(kind)) {
                    throw new IllegalArgumentException("kind inválido em " + id + ".");
                }
                String reviewLabel = optionalText(item, "review_label", "", 40);
                if (!"review".equals(kind) && !reviewLabel.isEmpty()) {
                    throw new IllegalArgumentException("review_label só pode ser usado em sessões kind=\"review\".");
                }

                sessions.add(new SessionItem(
                        id,
                        planId,
                        revision,
                        date,
                        subject,
                        topic,
                        minutes,
                        target,
                        kind,
                        reviewLabel,
                        "planned",
                        "plan",
                        0L
                ));
            }

            return new PlanPackage(planId, revision, title, objectiveName, objectiveDate, sessions);
        } catch (JSONException e) {
            throw new IllegalArgumentException("JSON inválido. Verifique vírgulas, aspas, tipos e chaves.");
        }
    }

    private static void rejectUnknownKeys(JSONObject object, String scope, String... allowedKeys) {
        Set<String> allowed = new HashSet<>();
        for (String key : allowedKeys) allowed.add(key);

        Iterator<String> keys = object.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            if (!allowed.contains(key)) {
                throw new IllegalArgumentException("Campo não suportado em " + scope + ": " + key + ".");
            }
        }
    }

    private static JSONObject requireObject(JSONObject object, String key) throws JSONException {
        if (!object.has(key) || object.isNull(key)) {
            throw new IllegalArgumentException("Campo obrigatório ausente: " + key);
        }
        Object value = object.get(key);
        if (!(value instanceof JSONObject)) {
            throw new IllegalArgumentException(key + " precisa ser um objeto.");
        }
        return (JSONObject) value;
    }

    private static JSONObject optionalObject(JSONObject object, String key) throws JSONException {
        if (!object.has(key)) return null;
        if (object.isNull(key)) {
            throw new IllegalArgumentException(key + " precisa ser um objeto quando informado.");
        }
        Object value = object.get(key);
        if (!(value instanceof JSONObject)) {
            throw new IllegalArgumentException(key + " precisa ser um objeto quando informado.");
        }
        return (JSONObject) value;
    }

    private static JSONArray requireArray(JSONObject object, String key) throws JSONException {
        if (!object.has(key) || object.isNull(key)) {
            throw new IllegalArgumentException("Campo obrigatório ausente: " + key);
        }
        Object value = object.get(key);
        if (!(value instanceof JSONArray)) {
            throw new IllegalArgumentException(key + " precisa ser uma lista.");
        }
        return (JSONArray) value;
    }

    private static String requiredText(JSONObject object, String key, int max) throws JSONException {
        if (!object.has(key) || object.isNull(key)) {
            throw new IllegalArgumentException("Campo obrigatório ausente: " + key);
        }
        Object raw = object.get(key);
        if (!(raw instanceof String)) {
            throw new IllegalArgumentException(key + " precisa ser texto.");
        }
        String value = ((String) raw).trim();
        if (value.isEmpty()) {
            throw new IllegalArgumentException("Campo obrigatório vazio: " + key);
        }
        rejectControlCharacters(value, key);
        if (value.length() > max) {
            throw new IllegalArgumentException(key + " excede " + max + " caracteres.");
        }
        return value;
    }

    private static String optionalNonBlankText(JSONObject object, String key, String fallback, int max) throws JSONException {
        if (!object.has(key)) return fallback;
        if (object.isNull(key)) {
            throw new IllegalArgumentException(key + " precisa ser texto quando informado.");
        }
        Object raw = object.get(key);
        if (!(raw instanceof String)) {
            throw new IllegalArgumentException(key + " precisa ser texto.");
        }
        String value = ((String) raw).trim();
        if (value.isEmpty()) value = fallback;
        rejectControlCharacters(value, key);
        if (value.length() > max) {
            throw new IllegalArgumentException(key + " excede " + max + " caracteres.");
        }
        return value;
    }

    private static String optionalText(JSONObject object, String key, String fallback, int max) throws JSONException {
        if (!object.has(key)) return fallback;
        if (object.isNull(key)) {
            throw new IllegalArgumentException(key + " precisa ser texto quando informado.");
        }
        Object raw = object.get(key);
        if (!(raw instanceof String)) {
            throw new IllegalArgumentException(key + " precisa ser texto.");
        }
        String value = ((String) raw).trim();
        rejectControlCharacters(value, key);
        if (value.length() > max) {
            throw new IllegalArgumentException(key + " excede " + max + " caracteres.");
        }
        return value;
    }

    private static void rejectControlCharacters(String value, String field) {
        for (int i = 0; i < value.length(); i++) {
            if (Character.isISOControl(value.charAt(i))) {
                throw new IllegalArgumentException(field + " contém caractere de controle não permitido.");
            }
        }
    }

    private static int requiredInteger(JSONObject object, String key) throws JSONException {
        if (!object.has(key) || object.isNull(key)) {
            throw new IllegalArgumentException("Campo obrigatório ausente: " + key);
        }
        Object raw = object.get(key);
        if (!(raw instanceof Integer) && !(raw instanceof Long)) {
            throw new IllegalArgumentException(key + " precisa ser um número inteiro.");
        }
        long value = ((Number) raw).longValue();
        if (value < Integer.MIN_VALUE || value > Integer.MAX_VALUE) {
            throw new IllegalArgumentException(key + " está fora do intervalo aceito.");
        }
        return (int) value;
    }

    private static void requireIsoDate(String value, String field) {
        if (!value.matches("\\d{4}-\\d{2}-\\d{2}")) {
            throw new IllegalArgumentException(field + " deve usar AAAA-MM-DD.");
        }
        SimpleDateFormat format = new SimpleDateFormat("yyyy-MM-dd", Locale.US);
        format.setLenient(false);
        try {
            format.parse(value);
        } catch (ParseException e) {
            throw new IllegalArgumentException(field + " deve usar AAAA-MM-DD.");
        }
    }

    public static String aiSpecification(String request) {
        return aiSpecification(
                request,
                StudyRepository.iso(new java.util.Date()),
                "",
                "",
                5,
                60,
                true,
                true,
                true,
                "",
                0
        );
    }

    public static String aiSpecification(
            String request,
            String today,
            String objectiveName,
            String objectiveDate,
            int dailyHours,
            int blockMinutes,
            boolean reviewD1,
            boolean reviewD3,
            boolean reviewD7,
            String activePlanId,
            int activeRevision
    ) {
        String userRequest = request == null ? "" : request.trim();
        String safeToday = today == null ? "" : today.trim();
        String safeObjective = objectiveName == null ? "" : objectiveName.trim();
        String safeObjectiveDate = objectiveDate == null ? "" : objectiveDate.trim();
        String safePlanId = activePlanId == null ? "" : activePlanId.trim();
        int safeHours = Math.max(1, Math.min(12, dailyHours));
        int safeBlock = Math.max(30, Math.min(180, blockMinutes));
        boolean automaticReviewsEnabled = reviewD1 || reviewD3 || reviewD7;
        String enabledReviews = reviewList(reviewD1, reviewD3, reviewD7);

        return "Você é o planejador de estudos do aplicativo Rota. " +
                "Sua tarefa é montar um CALENDÁRIO EXECUTÁVEL e completo, não uma lista ilustrativa de tópicos. " +
                "Responda SOMENTE com um único objeto JSON válido, sem markdown, sem comentários e sem texto antes ou depois.\n\n" +

                "CONTEXTO DO ROTA\n" +
                "- Data de hoje: " + valueOr(safeToday, "não informada") + ".\n" +
                "- Objetivo atual: " + valueOr(safeObjective, "não definido") + ".\n" +
                "- Data atual do objetivo: " + valueOr(safeObjectiveDate, "não definida") + ".\n" +
                "- Limite diário configurado: " + safeHours + " horas.\n" +
                "- Bloco preferido: " + safeBlock + " minutos.\n" +
                "- Revisões automáticas do app: " + enabledReviews + ".\n" +
                "- Plano atual: id=" + valueOr(safePlanId, "nenhum") + ", revision=" + Math.max(0, activeRevision) + ".\n\n" +

                "REGRA MAIS IMPORTANTE SOBRE REVISÕES\n" +
                "O Rota cria as revisões espaçadas automaticamente a partir da DATA REAL em que cada sessão de estudo é concluída. " +
                "Portanto, NÃO gere sessões kind=\"review\" por conta própria, NÃO escreva tópicos como \"Revisão: ...\" e NÃO tente agendar D+1/D+3/D+7 no JSON. " +
                "As revisões automáticas devem nascer no app, não na IA. Só use kind=\"review\" se o pedido do usuário exigir explicitamente uma revisão fixa independente das revisões automáticas.\n" +
                (automaticReviewsEnabled
                        ? "Como há revisões automáticas ativas, não ocupe deliberadamente 100% da capacidade todos os dias: quando o volume de conteúdo permitir, reserve cerca de 15% do limite diário para as revisões que o Rota poderá acrescentar depois.\n\n"
                        : "As revisões automáticas estão desativadas; não reserve tempo para elas sem pedido do usuário.\n\n") +

                "COMO PLANEJAR\n" +
                "1. Interprete o pedido do usuário como um plano real até a data do objetivo, prova ou prazo informado. Se o usuário informar uma data de início diferente, respeite-a. Nunca invente uma data de prova que não foi informada; se não houver data conhecida, use objective.date como string vazia.\n" +
                "2. Não entregue um plano esparso. Distribua sessões em TODOS os dias em que o usuário disse que pode estudar. Respeite exatamente dias indisponíveis, trabalho, pausas e outras restrições mencionadas. Se a disponibilidade não estiver explícita, distribua de forma consistente e sem grandes lacunas sem motivo pedagógico.\n" +
                "3. Respeite o limite diário de " + safeHours + " horas e use blocos próximos de " + safeBlock + " minutos. Nunca ultrapasse o limite diário. Não crie sessões vazias apenas para preencher tempo; use a capacidade disponível para cobrir o conteúdo necessário com ritmo sustentável.\n" +
                "4. Cada sessão deve ter UM conteúdo concreto e uma meta mensurável. Evite metas vagas como \"estudar o assunto\" ou \"compreender melhor\". Prefira número de questões, exercícios, páginas, produção, resumo ativo ou critério de acerto.\n" +
                "5. Organize pré-requisitos antes de conteúdos dependentes. Se o usuário fornecer edital/lista de conteúdos, faça uma cobertura completa dos itens relevantes antes do prazo e dê mais espaço aos tópicos fundamentais ou explicitamente difíceis.\n" +
                "6. Use kind=\"study\" para estudo normal e kind=\"assessment\" para simulados, baterias ou testes. Avaliações podem aparecer periodicamente quando forem úteis, mas não substituem o estudo do conteúdo.\n" +
                "7. Evite repetir a mesma sessão em dias consecutivos sem progressão. Quando um assunto exigir mais de um bloco, avance por subtópicos, dificuldade, aplicação ou correção de erros.\n" +
                "8. Não coloque sessões antes da data de hoje. Se objective.date estiver preenchida, nenhuma sessão pode ficar depois dessa data.\n" +
                "9. Se o pedido for uma ALTERAÇÃO do plano atual, reutilize o mesmo plan.id e use revision=" + nextRevision(activeRevision) + ". Se for um NOVO objetivo/plano, crie um id estável novo e use revision=1.\n" +
                "10. Gere IDs de sessão únicos e estáveis. Uma revisão futura do mesmo plano deve poder manter IDs de sessões conceitualmente iguais e criar IDs novos apenas para sessões realmente novas. Nunca use \"" +
                ScheduleRules.RUNTIME_REVIEW_ID_MARKER + "\" dentro de um ID: esse marcador é reservado às revisões automáticas do Rota.\n\n" +

                "AUDITORIA OBRIGATÓRIA ANTES DE RESPONDER\n" +
                "Faça esta checagem internamente e corrija o JSON antes de enviá-lo: " +
                "(a) nenhuma sessão está no passado; " +
                "(b) nenhum dia ultrapassa " + (safeHours * 60) + " minutos; " +
                "(c) todos os dias de estudo permitidos foram usados de forma coerente com o volume de conteúdo; " +
                "(d) não existem revisões D+1/D+3/D+7 criadas pela IA; " +
                "(e) todos os IDs são únicos e não usam o marcador reservado; " +
                "(f) todas as metas são específicas; " +
                "(g) o escopo pedido foi coberto; " +
                "(h) se objective.date estiver preenchida, nenhuma sessão passa do prazo; " +
                "(i) a resposta final contém somente JSON válido.\n\n" +

                "FORMATO OBRIGATÓRIO\n" +
                "{\"format\":\"studyplan\",\"format_version\":\"0.2\"," +
                "\"plan\":{\"id\":\"id-estavel\",\"revision\":1,\"title\":\"Título\"}," +
                "\"objective\":{\"name\":\"Objetivo\",\"date\":\"2026-09-30\"}," +
                "\"sessions\":[{\"id\":\"sessao-001\",\"date\":\"2026-09-02\",\"subject\":\"Matéria\"," +
                "\"topic\":\"Conteúdo específico\",\"minutes\":60,\"target\":\"Meta objetiva e mensurável\",\"kind\":\"study\"}]}.\n" +
                "As datas do exemplo são apenas demonstrações: use as datas reais do contexto. Se não houver uma data real de objetivo, use \"date\":\"\". " +
                "Em StudyPlan 0.2, target é obrigatório e deve descrever uma meta concreta e mensurável. " +
                "Campos kind aceitos pelo formato: study, review, assessment. Pela regra acima, use review somente se o usuário pedir uma revisão fixa explicitamente. " +
                "Não inclua review_label em sessões study/assessment. Não gere código executável, URLs, scripts ou instruções fora do JSON.\n\n" +

                "PEDIDO DO USUÁRIO\n" + valueOr(userRequest, "Monte um plano coerente usando o contexto acima.");
    }

    private static int nextRevision(int activeRevision) {
        if (activeRevision < 1) return 1;
        if (activeRevision == Integer.MAX_VALUE) return Integer.MAX_VALUE;
        return activeRevision + 1;
    }

    private static String reviewList(boolean d1, boolean d3, boolean d7) {
        List<String> values = new ArrayList<>();
        if (d1) values.add("D+1");
        if (d3) values.add("D+3");
        if (d7) values.add("D+7");
        if (values.isEmpty()) return "desativadas";

        StringBuilder joined = new StringBuilder();
        for (String value : values) {
            if (joined.length() > 0) joined.append(", ");
            joined.append(value);
        }
        return joined.toString();
    }

    private static String valueOr(String value, String fallback) {
        return value == null || value.trim().isEmpty() ? fallback : value.trim();
    }
}
