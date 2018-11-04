
using Regicide.API;

namespace RegisterFunction
{
    public class RegisterRequest
    {
        public string Username { get; set; }
        public string PassHash { get; set; }
        public string DispName { get; set; }
        public string Email { get; set; }
    }

    public class RegisterResponse
    {
        public RegisterResult Result { get; set; }
        public Account Account { get; set; }
        public string Token { get; set; }
    }

}