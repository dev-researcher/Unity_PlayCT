namespace PlayCT.Research
{
    public interface ISessionInfo
    {
        string SessionId { get; }
        string ParticipantId { get; }
        string Condition { get; }
    }

    public sealed class SessionInfo : ISessionInfo
    {
        public string SessionId { get; set; }
        public string ParticipantId { get; set; }
        public string Condition { get; set; }

        public SessionInfo(string sessionId, string participantId, string condition)
        {
            SessionId = sessionId;
            ParticipantId = participantId;
            Condition = condition;
        }
    }
}
