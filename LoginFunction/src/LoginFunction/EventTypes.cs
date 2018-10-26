using System;


namespace LoginFunction
{

    public struct LoginRequest
    {
        public string Username;
        public string PassHash;
    }

    public enum LoginResult
    {
        BadRequest = 0,
        InvalidCredentials = 1,
        Success = 2,
        DatabaseError = 3
    }

    public struct LoginResponse
    {
        public LoginResult Result;
        public UInt32 Identifier;
        public string AuthToken;
    }

    public enum DatabaseLookupResult
    {
        Error = 0,
        Invalid = 1,
        Success = 2
    }

    public enum UpdateTokenResult
    {
        Error = 0,
        InUse = 1,
        InvalidUser = 2,
        Success = 3
    }
}