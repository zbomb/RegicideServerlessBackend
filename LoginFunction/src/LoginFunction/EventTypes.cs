
namespace LoginFunction
{

    public struct LoginRequest
    {
        public string Username { get; set; }
        public string PassHash { get; set; }
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
        public LoginResult Result { get; set; }
        public Regicide.API.Account Account { get; set; }
        public string AuthToken { get; set; }
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