using System;
using System.Security.Cryptography;
using System.IO;
using System.Collections.Generic;

namespace Regicide.API
{
    public class Account 
    {
        public BasicAccountInfo Info { get; set; }
        public List<Card> Cards { get; set; }
        public List<Deck> Decks { get; set; }
        public List<Achievement> Achievements { get; set; }
        public PrivateInfo PrivInfo { get; set; }

        public Account()
        {
            Info = null;
            Cards = null;
            Decks = null;
            Achievements = null;
            PrivInfo = null;
        }
    }

    public class PrivateInfo
    {
        public string PassHash { get; set; }
    }

    public class BasicAccountInfo
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public UInt64 Coins { get; set; }
        public string DisplayName { get; set; }
        public bool Verified { get; set; }
    }

    public class Card
    {
        public UInt16 Id { get; set; }
        public UInt16 Ct { get; set; }
    }

    public class Deck
    {
        public UInt16 Id { get; set; }
        public string Name { get; set; }

        public List<Card> Cards { get; set; }
    }

    public class Achievement
    {
        public UInt16 Id { get; set; }
        public bool Complete { get; set; }
        public Int32 State { get; set; }
    }

    public enum AccountProperty
    {
        Info = 0,
        Cards = 1,
        Decks = 2,
        Achievements = 3,
        All = 4
    }

    internal enum AccountEntry
    {
        BasicInfo = 0,
        CardsLower = 1,
        CardsUpper = 2,
        Achievements = 3,
        Reserved = 4
        // Number 4 is reserved for future use
        // Anything thats > 4 is a deck with the Id = PropertyIndex - 4, PropertyIndex = Id + 4
        // This allows us to store all decks, as properties of the main account
    }

    public static class Constants
    {
        // Card lists are split into two chunks before being written to the database
        // This constant is the max ID number for the lower chunk, anything higher than
        // this will be put into the upper chunk instead
        public static readonly UInt16 MaxLowerCardId = 32767;

        public static readonly int UsernameMinLength = 5;
        public static readonly int UsernameMaxLength = 32;
        public static readonly int PasswordHashMinLength  = 40;
        public static readonly int EmailMinLength = 3;
        public static readonly int EmailMaxLength = 255;
        public static readonly int DispNameMinLength = 5;
        public static readonly int DispNameMaxLength = 48;
    }

    public static partial class Utils
    {
        public static string HashPassword( string inPass, byte[] inSalt )
        {
            if( String.IsNullOrEmpty( inPass ) || ( inSalt?.Length ?? 0 ) < 32 )
                return null;

            string Output = null;
            using( var HashAlgo = SHA256.Create() )
            {
                // Salt password 
                byte[] PassBytes = Convert.FromBase64String( inPass );
                byte[] PassBuffer = new byte[ PassBytes.Length + 32 ];
                Array.ConstrainedCopy( PassBytes, 0, PassBuffer, 0, PassBytes.Length );
                Array.ConstrainedCopy( inSalt, 0, PassBuffer, PassBuffer.Length - inSalt.Length, inSalt.Length );

                // Hash password
                byte[] HashedPass = HashAlgo.ComputeHash( PassBuffer );
                if( HashedPass == null || HashedPass.Length < 32 )
                    throw new Exception( "Failed to rehash password" );

                // Convert to base 64
                Output = Convert.ToBase64String( HashedPass );
            }

            return Output;
        }
    }
}
