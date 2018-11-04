using System;


namespace LogoutFunction
{

    public enum LogoutResult
    {
        Error = 0,
        InvalidToken = 1,
        Success = 2
    }

    public struct LogoutRequest
    {
        public string AuthToken { get; set; }
    }

    public struct LogoutResponse
    {
        public LogoutResult Result { get; set; }
    }
}