package app.rota.estudos;

public final class SessionItem {
    public final String id;
    public final String planId;
    public final int planRevision;
    public final String date;
    public final String subject;
    public final String topic;
    public final int minutes;
    public final String target;
    public final String kind;
    public final String reviewLabel;
    public final String status;
    public final String origin;
    public final long completedAt;

    public SessionItem(
            String id,
            String planId,
            int planRevision,
            String date,
            String subject,
            String topic,
            int minutes,
            String target,
            String kind,
            String reviewLabel,
            String status,
            String origin,
            long completedAt
    ) {
        this.id = id;
        this.planId = planId;
        this.planRevision = planRevision;
        this.date = date;
        this.subject = subject;
        this.topic = topic;
        this.minutes = minutes;
        this.target = target;
        this.kind = kind;
        this.reviewLabel = reviewLabel;
        this.status = status;
        this.origin = origin;
        this.completedAt = completedAt;
    }

    public boolean isCompleted() {
        return "completed".equals(status);
    }

    public boolean isReview() {
        return "review".equals(kind);
    }
}
