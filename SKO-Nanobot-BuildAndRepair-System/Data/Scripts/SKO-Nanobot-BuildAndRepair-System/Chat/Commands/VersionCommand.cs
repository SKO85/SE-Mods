namespace SKONanobotBuildAndRepairSystem.Chat.Commands
{
    public static class VersionCommand
    {
        /// <summary>
        /// Server-side: returns the mod version running on the server so clients can
        /// compare it against their own version and detect inconsistent installs.
        /// The client line is emitted locally in ChatHandler before the request is
        /// forwarded; this only produces the server line.
        /// </summary>
        public static ChatCommandResult Execute()
        {
            return ChatCommandResult.Success(string.Format("BaR Mod Server: v{0} (build {1})", Constants.ModVersion, Constants.BuildId));
        }
    }
}
