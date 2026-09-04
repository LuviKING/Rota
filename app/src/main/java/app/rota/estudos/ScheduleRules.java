package app.rota.estudos;

import java.util.List;
import java.util.Map;
import java.util.TreeMap;

public final class ScheduleRules {
    public static final String RUNTIME_REVIEW_ID_MARKER = "::review::";

    private ScheduleRules() {}

    public static int[] enabledReviewDays(boolean d1, boolean d3, boolean d7) {
        int count = (d1 ? 1 : 0) + (d3 ? 1 : 0) + (d7 ? 1 : 0);
        int[] result = new int[count];
        int i = 0;
        if (d1) result[i++] = 1;
        if (d3) result[i++] = 3;
        if (d7) result[i] = 7;
        return result;
    }

    public static int reviewMinutes(int originalMinutes) {
        return Math.max(15, Math.min(35, originalMinutes / 3));
    }

    public static boolean isDateOnOrAfter(String date, String cutoff) {
        return date != null && cutoff != null && date.compareTo(cutoff) >= 0;
    }

    public static boolean hasSessionOnOrAfter(List<SessionItem> sessions, String cutoff) {
        if (sessions == null || cutoff == null) return false;
        for (SessionItem session : sessions) {
            if (session != null && isDateOnOrAfter(session.date, cutoff)) return true;
        }
        return false;
    }

    public static String firstOverloadedDate(List<SessionItem> sessions, int maxMinutesPerDay) {
        if (sessions == null || maxMinutesPerDay <= 0) return "";

        Map<String, Long> totals = new TreeMap<>();
        for (SessionItem session : sessions) {
            if (session == null || session.date == null) continue;
            Long current = totals.get(session.date);
            long next = (current == null ? 0L : current) + Math.max(0L, (long) session.minutes);
            totals.put(session.date, next);
        }

        for (Map.Entry<String, Long> entry : totals.entrySet()) {
            if (entry.getValue() > (long) maxMinutesPerDay) return entry.getKey();
        }
        return "";
    }
}
