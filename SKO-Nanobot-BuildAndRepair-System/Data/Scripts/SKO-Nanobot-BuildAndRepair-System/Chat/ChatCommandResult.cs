namespace SKONanobotBuildAndRepairSystem.Chat
{
    public class ChatCommandResult
    {
        public string Message { get; set; }
        public bool IsError { get; set; }
        public bool UseMissionScreen { get; set; }
        public string ScreenTitle { get; set; }
        public string ScreenSubtitle { get; set; }

        public ChatCommandResult(string message, bool isError = false)
        {
            Message = message;
            IsError = isError;
        }

        public static ChatCommandResult Error(string message)
        {
            return new ChatCommandResult(message, true);
        }

        public static ChatCommandResult Success(string message)
        {
            return new ChatCommandResult(message);
        }

        public static ChatCommandResult MissionScreen(string message, string title, string subtitle)
        {
            return new ChatCommandResult(message)
            {
                UseMissionScreen = true,
                ScreenTitle = title,
                ScreenSubtitle = subtitle
            };
        }
    }
}
