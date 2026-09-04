package app.rota.estudos;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Calendar;
import java.util.Date;
import java.util.List;
import java.util.Locale;

public final class StudyRepository extends SQLiteOpenHelper {
    private static final String DB_NAME = "rota.db";
    private static final int DB_VERSION = 4;
    private static final Locale PT_BR = Locale.forLanguageTag("pt-BR");
    private static final String PLAN_REVISION_SETTING_PREFIX = "plan_revision::";

    public StudyRepository(Context context) {
        super(context, DB_NAME, null, DB_VERSION);
    }

    @Override
    public void onCreate(SQLiteDatabase db) {
        createSessionsTable(db, "sessions");
        db.execSQL("CREATE INDEX idx_sessions_date ON sessions(date)");
        db.execSQL("CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        putSetting(db, "review_d1", "1");
        putSetting(db, "review_d3", "1");
        putSetting(db, "review_d7", "1");
        putSetting(db, "daily_hours", "5");
        putSetting(db, "block_minutes", "60");
        putSetting(db, "active_plan_id", "");
        putSetting(db, "active_plan_revision", "0");
        putSetting(db, "active_plan_title", "");
        putSetting(db, "objective_name", "Meu objetivo");
        putSetting(db, "objective_date", "");
    }

    @Override
    public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) {
        if (oldVersion < 2) {
            try {
                db.execSQL("ALTER TABLE sessions ADD COLUMN origin TEXT NOT NULL DEFAULT 'plan'");
            } catch (Exception ignored) {
                // Existing development installs may already contain the column.
            }
        }
        if (oldVersion < 3) {
            removePrototypeData(db);
        }
        if (oldVersion < 4) {
            migrateSessionsToPlanScopedIdentity(db);
        }
    }

    private static void createSessionsTable(SQLiteDatabase db, String tableName) {
        db.execSQL("CREATE TABLE " + tableName + " (" +
                "id TEXT NOT NULL," +
                "plan_id TEXT NOT NULL," +
                "plan_revision INTEGER NOT NULL," +
                "date TEXT NOT NULL," +
                "subject TEXT NOT NULL," +
                "topic TEXT NOT NULL," +
                "minutes INTEGER NOT NULL," +
                "target TEXT NOT NULL DEFAULT ''," +
                "kind TEXT NOT NULL DEFAULT 'study'," +
                "review_label TEXT NOT NULL DEFAULT ''," +
                "status TEXT NOT NULL DEFAULT 'planned'," +
                "origin TEXT NOT NULL DEFAULT 'plan'," +
                "completed_at INTEGER NOT NULL DEFAULT 0," +
                "PRIMARY KEY(plan_id,id)" +
                ")");
    }

    private static void migrateSessionsToPlanScopedIdentity(SQLiteDatabase db) {
        createSessionsTable(db, "sessions_v4");
        db.execSQL(
                "INSERT INTO sessions_v4 (" +
                        "id,plan_id,plan_revision,date,subject,topic,minutes,target,kind,review_label,status,origin,completed_at" +
                        ") SELECT " +
                        "id,plan_id,plan_revision,date,subject,topic,minutes,target,kind,review_label,status,origin,completed_at " +
                        "FROM sessions"
        );
        db.execSQL("DROP TABLE sessions");
        db.execSQL("ALTER TABLE sessions_v4 RENAME TO sessions");
        db.execSQL("CREATE INDEX idx_sessions_date ON sessions(date)");
    }

    private static void removePrototypeData(SQLiteDatabase db) {
        boolean prototypeWasActive = "demo-iff-2026".equals(getSetting(db, "active_plan_id", ""));

        // Pending prototype content is disposable; completed rows are immutable history.
        db.delete(
                "sessions",
                "plan_id=? AND status!='completed'",
                new String[]{"demo-iff-2026"}
        );

        // Never rewrite settings that may already belong to a real imported plan.
        if (prototypeWasActive) {
            putSetting(db, "active_plan_id", "");
            putSetting(db, "active_plan_revision", "0");
            putSetting(db, "active_plan_title", "");
            if ("IFF Engenharia Mecânica".equals(getSetting(db, "objective_name", ""))) {
                putSetting(db, "objective_name", "Meu objetivo");
            }
            if ("2026-11-22".equals(getSetting(db, "objective_date", ""))) {
                putSetting(db, "objective_date", "");
            }
        }
    }

    /** Opens the database and runs migrations. Kept for compatibility with older UI code. */
    public void ensureSeedData() {
        getWritableDatabase();
    }

    public List<SessionItem> sessionsForDate(String date) {
        List<SessionItem> result = new ArrayList<>();
        try (Cursor c = getReadableDatabase().query(
                "sessions", null, "date=?", new String[]{date}, null, null,
                "CASE status WHEN 'completed' THEN 1 ELSE 0 END, CASE kind WHEN 'review' THEN 0 WHEN 'assessment' THEN 2 ELSE 1 END, rowid"
        )) {
            while (c.moveToNext()) result.add(fromCursor(c));
        }
        return result;
    }

    public SessionItem nextSession(String fromDate) {
        try (Cursor c = getReadableDatabase().query(
                "sessions", null,
                "date>=? AND status!='completed'",
                new String[]{fromDate}, null, null,
                "date ASC, CASE kind WHEN 'review' THEN 0 ELSE 1 END, rowid ASC", "1"
        )) {
            return c.moveToFirst() ? fromCursor(c) : null;
        }
    }

    public int[] progressForDate(String date) {
        int total = 0;
        int completed = 0;
        try (Cursor c = getReadableDatabase().rawQuery(
                "SELECT status, COUNT(*) FROM sessions WHERE date=? GROUP BY status", new String[]{date})) {
            while (c.moveToNext()) {
                int count = c.getInt(1);
                total += count;
                if ("completed".equals(c.getString(0))) completed += count;
            }
        }
        return new int[]{completed, total};
    }

    public int totalMinutesForDate(String date) {
        try (Cursor c = getReadableDatabase().rawQuery(
                "SELECT COALESCE(SUM(minutes),0) FROM sessions WHERE date=?", new String[]{date})) {
            c.moveToFirst();
            return c.getInt(0);
        }
    }

    /**
     * Compatibility entry point for callers that only know a session id. If an id is shared
     * by multiple plans, the active plan wins; otherwise completion only proceeds when the id
     * identifies exactly one pending row.
     */
    public boolean markCompleted(String sessionId) {
        if (sessionId == null || sessionId.trim().isEmpty()) return false;
        SQLiteDatabase db = getWritableDatabase();
        String planId = resolvePlanIdForCompletion(db, sessionId);
        return planId != null && markCompleted(planId, sessionId);
    }

    public boolean markCompleted(String planId, String sessionId) {
        if (planId == null || planId.trim().isEmpty() || sessionId == null || sessionId.trim().isEmpty()) {
            return false;
        }

        SQLiteDatabase db = getWritableDatabase();
        db.beginTransaction();
        try {
            SessionItem item = findByIdentity(db, planId, sessionId);
            if (item == null || item.isCompleted()) return false;

            long now = System.currentTimeMillis();
            ContentValues values = new ContentValues();
            values.put("status", "completed");
            values.put("completed_at", now);
            int updated = db.update(
                    "sessions",
                    values,
                    "plan_id=? AND id=? AND status!='completed'",
                    new String[]{planId, sessionId}
            );
            if (updated != 1) return false;

            if ("study".equals(item.kind)) {
                boolean d1 = boolSetting(db, "review_d1", true);
                boolean d3 = boolSetting(db, "review_d3", true);
                boolean d7 = boolSetting(db, "review_d7", true);
                int[] days = ScheduleRules.enabledReviewDays(d1, d3, d7);
                Calendar completedDate = Calendar.getInstance();
                completedDate.setTimeInMillis(now);
                for (int day : days) {
                    Calendar reviewDate = (Calendar) completedDate.clone();
                    reviewDate.add(Calendar.DAY_OF_YEAR, day);
                    String reviewId = item.id + ScheduleRules.RUNTIME_REVIEW_ID_MARKER + day;
                    if (findByIdentity(db, item.planId, reviewId) != null) continue;
                    SessionItem review = new SessionItem(
                            reviewId,
                            item.planId,
                            item.planRevision,
                            iso(reviewDate.getTime()),
                            item.subject,
                            item.topic,
                            ScheduleRules.reviewMinutes(item.minutes),
                            "Recupere de memória e faça 5 questões.",
                            "review",
                            "D+" + day,
                            "planned",
                            "runtime",
                            0L
                    );
                    insertOrReplace(db, review);
                }
            }

            db.setTransactionSuccessful();
            return true;
        } finally {
            db.endTransaction();
        }
    }

    private static String resolvePlanIdForCompletion(SQLiteDatabase db, String sessionId) {
        String activePlanId = getSetting(db, "active_plan_id", "");
        if (!activePlanId.isEmpty()) {
            SessionItem active = findByIdentity(db, activePlanId, sessionId);
            if (active != null && !active.isCompleted()) return activePlanId;
        }

        String onlyPlanId = null;
        int count = 0;
        try (Cursor c = db.query(
                "sessions",
                new String[]{"plan_id"},
                "id=? AND status!='completed'",
                new String[]{sessionId},
                null,
                null,
                "plan_id ASC",
                "2"
        )) {
            while (c.moveToNext()) {
                onlyPlanId = c.getString(0);
                count++;
            }
        }
        return count == 1 ? onlyPlanId : null;
    }

    public ApplyResult applyPlan(PlanPackage plan) {
        SQLiteDatabase db = getWritableDatabase();
        db.beginTransaction();
        try {
            String currentPlanId = getSetting(db, "active_plan_id", "");
            int currentRevision = intSetting(db, "active_plan_revision", 0);
            int knownRevision = Math.max(
                    intSetting(db, revisionSettingKey(plan.planId), 0),
                    maxStoredRevision(db, plan.planId)
            );
            if (plan.planId.equals(currentPlanId)) {
                knownRevision = Math.max(knownRevision, currentRevision);
            }
            if (plan.revision <= knownRevision) {
                return new ApplyResult(false, 0, 0,
                        "A revisão importada precisa ser maior que a maior revisão já aplicada deste plano (" +
                                knownRevision + ").");
            }

            String today = iso(new Date());
            List<SessionItem> eligible = eligibleIncomingSessions(db, plan.sessions, today);
            if (eligible.isEmpty()) {
                return new ApplyResult(false, 0, 0,
                        "O plano não contém nenhuma sessão futura que possa ser aplicada com segurança.");
            }

            int dailyLimitMinutes = Math.max(1, Math.min(12, intSetting(db, "daily_hours", 5))) * 60;
            List<SessionItem> capacityLoad = new ArrayList<>(eligible);
            capacityLoad.addAll(protectedSessionsForCapacity(db, today));
            String overloadedDate = ScheduleRules.firstOverloadedDate(capacityLoad, dailyLimitMinutes);
            if (!overloadedDate.isEmpty()) {
                return new ApplyResult(false, 0, 0,
                        "O plano, somado ao histórico já concluído e às revisões automáticas preservadas, " +
                                "ultrapassa seu limite diário em " + shortDate(overloadedDate) +
                                ". Ajuste o plano ou o limite diário.");
            }

            int removed = db.delete(
                    "sessions",
                    "origin='plan' AND status!='completed' AND date>=?",
                    new String[]{today}
            );

            for (SessionItem incoming : eligible) {
                insertOrReplace(db, incoming);
            }

            putSetting(db, "active_plan_id", plan.planId);
            putSetting(db, "active_plan_revision", String.valueOf(plan.revision));
            putSetting(db, "active_plan_title", plan.title);
            putSetting(db, "objective_name", plan.objectiveName);
            putSetting(db, "objective_date", plan.objectiveDate);
            putSetting(db, revisionSettingKey(plan.planId), String.valueOf(plan.revision));

            db.setTransactionSuccessful();
            return new ApplyResult(true, eligible.size(), removed, "Plano aplicado com segurança.");
        } finally {
            db.endTransaction();
        }
    }

    private List<SessionItem> eligibleIncomingSessions(SQLiteDatabase db, List<SessionItem> incomingSessions, String today) {
        List<SessionItem> eligible = new ArrayList<>();
        for (SessionItem incoming : incomingSessions) {
            if (incoming == null || !ScheduleRules.isDateOnOrAfter(incoming.date, today)) continue;

            SessionItem existing = findByIdentity(db, incoming.planId, incoming.id);
            if (existing != null && (existing.isCompleted()
                    || "runtime".equals(existing.origin)
                    || !ScheduleRules.isDateOnOrAfter(existing.date, today))) {
                continue;
            }
            eligible.add(incoming);
        }
        return eligible;
    }

    private List<SessionItem> protectedSessionsForCapacity(SQLiteDatabase db, String fromDate) {
        List<SessionItem> result = new ArrayList<>();
        try (Cursor c = db.query(
                "sessions",
                null,
                "date>=? AND (status='completed' OR origin='runtime')",
                new String[]{fromDate},
                null,
                null,
                "date ASC, rowid ASC"
        )) {
            while (c.moveToNext()) result.add(fromCursor(c));
        }
        return result;
    }

    private static int maxStoredRevision(SQLiteDatabase db, String planId) {
        try (Cursor c = db.rawQuery(
                "SELECT COALESCE(MAX(plan_revision),0) FROM sessions WHERE plan_id=?",
                new String[]{planId}
        )) {
            return c.moveToFirst() ? c.getInt(0) : 0;
        }
    }

    private static String revisionSettingKey(String planId) {
        return PLAN_REVISION_SETTING_PREFIX + planId;
    }

    public String setting(String key, String fallback) {
        return getSetting(getReadableDatabase(), key, fallback);
    }

    public int intSetting(String key, int fallback) {
        return intSetting(getReadableDatabase(), key, fallback);
    }

    public boolean boolSetting(String key, boolean fallback) {
        return boolSetting(getReadableDatabase(), key, fallback);
    }

    public void savePreferences(String objectiveName, String objectiveDate, int dailyHours, int blockMinutes,
                                boolean d1, boolean d3, boolean d7) {
        SQLiteDatabase db = getWritableDatabase();
        db.beginTransaction();
        try {
            putSetting(db, "objective_name", clean(objectiveName, "Meu objetivo"));
            putSetting(db, "objective_date", objectiveDate == null ? "" : objectiveDate.trim());
            putSetting(db, "daily_hours", String.valueOf(Math.max(1, Math.min(12, dailyHours))));
            putSetting(db, "block_minutes", String.valueOf(Math.max(30, Math.min(180, blockMinutes))));
            putSetting(db, "review_d1", d1 ? "1" : "0");
            putSetting(db, "review_d3", d3 ? "1" : "0");
            putSetting(db, "review_d7", d7 ? "1" : "0");
            db.setTransactionSuccessful();
        } finally {
            db.endTransaction();
        }
    }

    private static String clean(String value, String fallback) {
        if (value == null || value.trim().isEmpty()) return fallback;
        String trimmed = value.trim();
        return trimmed.length() > 120 ? trimmed.substring(0, 120) : trimmed;
    }

    private static SessionItem findByIdentity(SQLiteDatabase db, String planId, String id) {
        try (Cursor c = db.query(
                "sessions",
                null,
                "plan_id=? AND id=?",
                new String[]{planId, id},
                null,
                null,
                null,
                "1"
        )) {
            return c.moveToFirst() ? fromCursor(c) : null;
        }
    }

    private static void insertOrReplace(SQLiteDatabase db, SessionItem item) {
        ContentValues v = new ContentValues();
        v.put("id", item.id);
        v.put("plan_id", item.planId);
        v.put("plan_revision", item.planRevision);
        v.put("date", item.date);
        v.put("subject", item.subject);
        v.put("topic", item.topic);
        v.put("minutes", item.minutes);
        v.put("target", item.target);
        v.put("kind", item.kind);
        v.put("review_label", item.reviewLabel);
        v.put("status", item.status);
        v.put("origin", item.origin);
        v.put("completed_at", item.completedAt);
        long rowId = db.insertWithOnConflict("sessions", null, v, SQLiteDatabase.CONFLICT_REPLACE);
        if (rowId == -1L) {
            throw new IllegalStateException("Falha ao persistir a sessão " + item.id + ".");
        }
    }

    private static SessionItem fromCursor(Cursor c) {
        return new SessionItem(
                c.getString(c.getColumnIndexOrThrow("id")),
                c.getString(c.getColumnIndexOrThrow("plan_id")),
                c.getInt(c.getColumnIndexOrThrow("plan_revision")),
                c.getString(c.getColumnIndexOrThrow("date")),
                c.getString(c.getColumnIndexOrThrow("subject")),
                c.getString(c.getColumnIndexOrThrow("topic")),
                c.getInt(c.getColumnIndexOrThrow("minutes")),
                c.getString(c.getColumnIndexOrThrow("target")),
                c.getString(c.getColumnIndexOrThrow("kind")),
                c.getString(c.getColumnIndexOrThrow("review_label")),
                c.getString(c.getColumnIndexOrThrow("status")),
                c.getString(c.getColumnIndexOrThrow("origin")),
                c.getLong(c.getColumnIndexOrThrow("completed_at"))
        );
    }

    private static void putSetting(SQLiteDatabase db, String key, String value) {
        ContentValues v = new ContentValues();
        v.put("key", key);
        v.put("value", value == null ? "" : value);
        db.insertWithOnConflict("settings", null, v, SQLiteDatabase.CONFLICT_REPLACE);
    }

    private static String getSetting(SQLiteDatabase db, String key, String fallback) {
        try (Cursor c = db.query("settings", new String[]{"value"}, "key=?", new String[]{key}, null, null, null, "1")) {
            return c.moveToFirst() ? c.getString(0) : fallback;
        }
    }

    private static int intSetting(SQLiteDatabase db, String key, int fallback) {
        try {
            return Integer.parseInt(getSetting(db, key, String.valueOf(fallback)));
        } catch (NumberFormatException e) {
            return fallback;
        }
    }

    private static boolean boolSetting(SQLiteDatabase db, String key, boolean fallback) {
        String value = getSetting(db, key, fallback ? "1" : "0");
        return "1".equals(value) || "true".equalsIgnoreCase(value);
    }

    public static String iso(Date date) {
        return new SimpleDateFormat("yyyy-MM-dd", Locale.US).format(date);
    }

    public static Date parseIso(String value) {
        try {
            SimpleDateFormat f = new SimpleDateFormat("yyyy-MM-dd", Locale.US);
            f.setLenient(false);
            return f.parse(value);
        } catch (Exception e) {
            return new Date();
        }
    }

    public static String humanDate(String isoDate) {
        return new SimpleDateFormat("EEEE, d 'de' MMMM", PT_BR).format(parseIso(isoDate));
    }

    public static String shortDate(String isoDate) {
        return new SimpleDateFormat("dd/MM", PT_BR).format(parseIso(isoDate));
    }

    public static final class ApplyResult {
        public final boolean success;
        public final int inserted;
        public final int removed;
        public final String message;

        public ApplyResult(boolean success, int inserted, int removed, String message) {
            this.success = success;
            this.inserted = inserted;
            this.removed = removed;
            this.message = message;
        }
    }
}
