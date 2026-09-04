package app.rota.estudos;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public class StudyPlanPromptTest {
    @Test
    public void promptKeepsAutomaticReviewsInsideRota() {
        String prompt = StudyPlanImporter.aiSpecification(
                "Quero aprender pré-cálculo até o fim do mês.",
                "2026-09-01",
                "Pré-cálculo",
                "2026-09-30",
                2,
                60,
                true,
                true,
                true,
                "pre-calculo",
                3
        );

        assertTrue(prompt.contains("NÃO gere sessões kind=\"review\" por conta própria"));
        assertTrue(prompt.contains("DATA REAL"));
        assertTrue(prompt.contains("D+1, D+3, D+7"));
        assertTrue(prompt.contains("reserve cerca de 15%"));
        assertTrue(prompt.contains("revision=4"));
        assertTrue(prompt.contains("2 horas"));
        assertTrue(prompt.contains("60 minutos"));
        assertTrue(prompt.contains("nenhum dia ultrapassa 120 minutos"));
        assertTrue(prompt.contains(ScheduleRules.RUNTIME_REVIEW_ID_MARKER));
        assertTrue(prompt.contains("marcador é reservado às revisões automáticas do Rota"));
    }

    @Test
    public void promptRequiresExecutableCalendarAndJsonOnly() {
        String prompt = StudyPlanImporter.aiSpecification("Monte meu plano.");

        assertTrue(prompt.contains("CALENDÁRIO EXECUTÁVEL"));
        assertTrue(prompt.contains("Responda SOMENTE com um único objeto JSON válido"));
        assertTrue(prompt.contains("Não entregue um plano esparso"));
        assertTrue(prompt.contains("AUDITORIA OBRIGATÓRIA ANTES DE RESPONDER"));
        assertTrue(prompt.contains("Meta objetiva e mensurável"));
        assertFalse(prompt.contains("```"));
    }

    @Test
    public void promptDoesNotReserveReviewCapacityWhenReviewsAreDisabled() {
        String prompt = StudyPlanImporter.aiSpecification(
                "Monte um plano de álgebra.",
                "2026-09-01",
                "Álgebra",
                "",
                3,
                45,
                false,
                false,
                false,
                "",
                0
        );

        assertTrue(prompt.contains("Revisões automáticas do app: desativadas"));
        assertTrue(prompt.contains("não reserve tempo para elas"));
        assertFalse(prompt.contains("reserve cerca de 15%"));
    }

    @Test
    public void promptMakesObjectiveDeadlineUnambiguous() {
        String prompt = StudyPlanImporter.aiSpecification(
                "Plano até a prova.",
                "2026-09-01",
                "Pré-cálculo",
                "2026-09-30",
                2,
                60,
                true,
                true,
                true,
                "",
                0
        );

        assertTrue(prompt.contains("nenhuma sessão passa do prazo"));
        assertTrue(prompt.contains("use as datas reais do contexto"));
        assertTrue(prompt.contains("Se não houver uma data real de objetivo, use \"date\":\"\""));
        assertFalse(prompt.contains("AAAA-MM-DD ou vazio"));
    }
}
