using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Regicide.API;

namespace RegicideUtils
{
    public static class AccountGenerator
    {
        // Parameter Constraints
        static readonly int UsernameMinLength = 5;
        static readonly int UsernameMaxLength = 32;
        static readonly int PasswordHashLength = 44;
        static readonly int EmailMinLength = 3;
        static readonly int EmailMaxLength = 255;
        static readonly int DispNameMinLength = 5;
        static readonly int DispNameMaxLength = 48;

        public static void Init()
        {
            Program.AddCommand( new Command()
            {
                Name = "gen_account",
                Callback = GenerateAccount
            } );
        }

        public static bool GenerateAccount( string Args )
        {
            // Query Basic Info
            Console.WriteLine( "====== Account Generator v0.1 =======" );

            string LocalPath = AppDomain.CurrentDomain.BaseDirectory + "/GeneratedAccounts/";
            try
            {
                if( !Directory.Exists( LocalPath ) )
                    Directory.CreateDirectory( LocalPath );
            }
            catch( Exception Ex )
            {
                Console.WriteLine( "=> Failed to create output directory! {0}", Ex.Message );
                Console.WriteLine( "=> Utility output might not be able to write to disk" );
            }

            string FilePath = null;

        // Output File Name
        filename:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter output file name:" );
            Console.Write( "> " );

            string OutputFile = Console.ReadLine()?.Trim();
            if( OutputFile?.ToLower() == "q" || OutputFile?.ToLower() == "quit" )
                return false;

            if( String.IsNullOrWhiteSpace( OutputFile ) || !OutputFile.Contains( "." ) )
            {
                Console.WriteLine( "==> Please enter a valid output file name" );
                goto filename;
            }

            // Check if file exists
            if( File.Exists( LocalPath + "/" + OutputFile ) )
            {
                Console.WriteLine( "=> A file with this name already exists.. do you want to overwrite? (y/n)" );
            fileyn:
                Console.Write( "> " );

                string YN = Console.ReadLine()?.Trim();

                if( YN.ToLower() == "y" )
                {
                    FilePath = LocalPath + "/" + OutputFile;
                }
                else if( YN.ToLower() == "n" )
                {
                    goto filename;
                }
                else if( YN.ToLower() == "q" || YN.ToLower() == "quit" )
                {
                    return false;
                }
                else
                {
                    Console.WriteLine( "=> Unknown input.. enter 'y' to overwrite, or 'n' to enter a new filename" );
                    goto fileyn;
                }
            }
            else
            {
                FilePath = LocalPath + "/" + OutputFile;
            }

        // Username
        username:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter Username:" );
            Console.Write( "> " );

            string Username = Console.ReadLine();
            if( !String.IsNullOrEmpty( Username ) )
            {
                Username = Username.Trim();

                if( Username?.ToLower() == "q" || Username?.ToLower() == "quit" )
                    return false;

                if( String.IsNullOrEmpty( Username ) || Username.Length < UsernameMinLength || Username.Length > UsernameMaxLength
                   || !Regex.IsMatch( Username, "^[a-zA-Z0-9_-]+$" ) )
                {
                    Console.WriteLine( "==> Invalid username! Must be between {0} and {1} characters and be comprised of alpha numeric characters!", UsernameMinLength, UsernameMaxLength );
                    goto username;
                }
            }
            else
            {
                Username = null;
                Console.WriteLine( "==> Skipping username.." );
            }

        // Display Name
        dispname:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter Display Name:" );
            Console.Write( "> " );

            string DispName = Console.ReadLine();
            if( !String.IsNullOrEmpty( DispName ) )
            {
                DispName = DispName.Trim();

                if( DispName?.ToLower() == "q" || DispName?.ToLower() == "quit" )
                    return false;

                if( String.IsNullOrEmpty( DispName ) || DispName.Length < DispNameMinLength || DispName.Length > DispNameMaxLength
                   || Regex.IsMatch( DispName, @"\p{C}+" ) )
                {
                    Console.WriteLine( "==> Invalid display name! Must be between {0} and {1} characters and cant contain control/whitespace characters!", DispNameMinLength, DispNameMaxLength );
                    goto dispname;
                }
            }
            else
            {
                DispName = null;
                Console.WriteLine( "==> Skipping display name.." );
            }

        // Email Address
        email:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter Email Address:" );
            Console.Write( "> " );

            string Email = Console.ReadLine();
            if( !String.IsNullOrEmpty( Email ) )
            {
                Email = Email.Trim();

                if( Email?.ToLower() == "q" || Email?.ToLower() == "quit" )
                    return false;

                if( String.IsNullOrEmpty( Email ) || Email.Length < EmailMinLength || Email.Length > EmailMaxLength
                   || !Email.Contains( "@" ) )
                {
                    Console.WriteLine( "==> Invalid Email Address! Must be between {0} and {1} characters and requires the '@' symbol!", EmailMinLength, EmailMaxLength );
                    goto email;
                }
            }
            else
            {
                Email = null;
                Console.WriteLine( "==> Skipping Email Address.." );
            }


        // Password Hash
        password:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter Password:" );
            Console.Write( "> " );

            string RawPass = Console.ReadLine();
            string PassHash = null;
            if( !String.IsNullOrEmpty( RawPass ) )
            {
                if( RawPass?.ToLower() == "q" || RawPass?.ToLower() == "quit" )
                    return false;

                if( String.IsNullOrEmpty( RawPass ) || RawPass.Length < 5 )
                {
                    Console.WriteLine( "==> Invalid Password! Must be between at least 5 characters" );
                    goto password;
                }

                if( String.IsNullOrEmpty( Username ) || Username.Length < UsernameMinLength )
                {
                    Console.WriteLine( "==> You cannot enter a password for this account without entering a username!" );
                }
                else
                {
                    // Hash the password using sha256
                    byte[] PassBuffer = new byte[ Encoding.UTF8.GetByteCount( RawPass ) + 5 ];
                    Array.ConstrainedCopy( Encoding.UTF8.GetBytes( RawPass ), 0, PassBuffer, 0, PassBuffer.Length - 5 );
                    byte[] UserBytes = Encoding.UTF8.GetBytes( Username.ToLower() );
                    Array.Reverse( UserBytes );
                    Array.ConstrainedCopy( UserBytes, 0, PassBuffer, PassBuffer.Length - 5, 5 );

                    using( var HashAlgo = SHA256.Create() )
                    {
                        for( int i = 0; i < 3; i++ )
                        {
                            PassBuffer = HashAlgo.ComputeHash( PassBuffer );
                        }
                    }

                    PassHash = Convert.ToBase64String( PassBuffer );
                }
            }
            else
            {
                RawPass = null;
                PassHash = null;
                Console.WriteLine( "==> Skipping Password.." );
            }

        // Coins
        coins:
            Console.WriteLine( "" );
            Console.WriteLine( "Enter Coins:" );
            Console.Write( "> " );

            string CoinsStr = Console.ReadLine();
            UInt64 Coins = 0;
            bool bCoinsSet = false;

            if( !String.IsNullOrEmpty( CoinsStr ) )
            {
                CoinsStr = CoinsStr.Trim();

                if( CoinsStr?.ToLower() == "q" || CoinsStr?.ToLower() == "quit" )
                    return false;

                if( !UInt64.TryParse( CoinsStr, out Coins ) )
                {
                    Console.WriteLine( "==> Invalid coin amount entered.. must be between 0 and {0}", UInt64.MaxValue );
                    Coins = 0;
                    goto coins;
                }

                bCoinsSet = true;
            }
            else
            {
                CoinsStr = null;
                Coins = 0;
                bCoinsSet = false;
                Console.WriteLine( "==> Skipping Coins.." );
            }

        // Verify State
        verify:
            Console.WriteLine( "" );
            Console.WriteLine( "Should account be verified? (Enter nothing to skip)" );
            Console.Write( "> " );

            string VerifyStr = Console.ReadLine();
            bool bSetVerify = false;
            bool Verify = false;

            if( !String.IsNullOrEmpty( VerifyStr ) )
            {
                VerifyStr = VerifyStr.Trim();

                if( VerifyStr.ToLower() == "q" || VerifyStr.ToLower() == "quit" )
                    return false;

                if( !Boolean.TryParse( VerifyStr, out Verify ) )
                {
                    Console.WriteLine( "==> Invalid verify state entered! Try again.." );
                    Verify = false;
                    goto verify;
                }

                bSetVerify = true;
            }
            else
            {
                bSetVerify = false;
                Verify = false;
                VerifyStr = null;

                Console.WriteLine( "==> Skipping Verify State.." );
            }


            // Card List
            Console.WriteLine( "" );
            Console.WriteLine( "=> Do you want to add cards to this account? (y/n) [q to exit account builder]" );
            Console.Write( "> " );

            List<Card> Cards = new List<Card>();
            bool bCardsSet = false;

        cardyn:
            string CardYN = Console.ReadLine()?.Trim()?.ToLower();

            if( String.IsNullOrEmpty( CardYN ) || CardYN == "n" )
            {
                Console.WriteLine( "=> Skipping cards.." );
            }
            else if( CardYN == "y" )
            {
                bCardsSet = true;
                Cards = RunCardEditor();
            }
            else if( CardYN == "q" || CardYN == "quit" )
            {
                return false;
            }
            else
            {
                Console.WriteLine( "=> Unknown response.. enter 'y' to enter card editor, or 'n' to skip" );
                goto cardyn;
            }


            // Deck Editor
            Console.WriteLine( "" );
            Console.WriteLine( "=> Do you want to add decks to this account? (y/n) [q to exit account builder]" );
            Console.Write( "> " );

            List<Deck> Decks = new List<Deck>();
            bool bDecksSet = false;

        deckyn:
            string DeckYN = Console.ReadLine()?.Trim()?.ToLower();

            if( String.IsNullOrEmpty( DeckYN ) || DeckYN == "n" )
            {
                Console.WriteLine( "=> Skipping decks.." );
            }
            else if( DeckYN == "y" )
            {
                bDecksSet = true;
                Console.WriteLine( "=====> Deck Editor <=====" );
                Console.WriteLine( "=> To exit the editor.. type 'exit' at any point" );
                while( true )
                {
                    Console.WriteLine( "" );
                    if( Decks.Count >= 32 )
                    {
                        Console.WriteLine( "=> Maximum number of decks (32) hit.. exiting deck editor!" );
                        break;
                    }

                    // Get Id Number
                    Console.WriteLine( "=> Enter Deck ID: " );
                    Console.Write( "> " );

                    string IdStr = Console.ReadLine()?.Trim();

                    if( IdStr?.ToLower() == "exit" )
                    {
                        Console.WriteLine( "=> Exiting deck editor.." );
                        break;
                    }
                    if( String.IsNullOrEmpty( IdStr ) || !UInt16.TryParse( IdStr, out UInt16 Id ) || Id < 1 || Id > 32 )
                    {
                        Console.WriteLine( "=> Invalid deck identifier! Must be between 1 and 32" );
                        continue;
                    }

                // Get Name
                inname:
                    Console.WriteLine( "" );
                    Console.WriteLine( "=> Enter Deck Name: " );
                    Console.Write( "> " );

                    string DeckName = Console.ReadLine()?.Trim();

                    if( DeckName?.ToLower() == "exit" )
                    {
                        Console.WriteLine( "=> Exiting deck editor..." );
                        break;
                    }
                    if( String.IsNullOrWhiteSpace( DeckName ) || DeckName.Length < 3 || DeckName.Length > 64 )
                    {
                        Console.WriteLine( "=> Invalid deck name! Must be between 3 and 64 characters" );
                        goto inname;
                    }

                    Console.WriteLine( "=> Enter Cards For This Deck:" );
                    var DeckCards = RunCardEditor( 100 );

                    Decks.Add( new Deck()
                    {
                        Cards = DeckCards,
                        Name = DeckName,
                        Id = Id
                    } );

                    Console.WriteLine( "=> New deck added ({0}) [{1}] with {2} cards", Id, DeckName, DeckCards.Count );
                }
            }
            else if( DeckYN == "q" || DeckYN == "quit" )
            {
                return false;
            }
            else
            {
                Console.WriteLine( "=> Unknown response.. enter 'y' to enter deck editor, or 'n' to skip" );
                goto deckyn;
            }

            // Were going to skip achievements for now
            Console.WriteLine( "" );
            Console.WriteLine( "=> Achievement editor has not yet been created" );

            // TODO: Achievements

            Console.WriteLine( "" );
            Console.WriteLine( "==> Account complete! Building and serializing..." );
            Console.WriteLine( "==> Output will be written to: {0}", FilePath );

            // Complete!
            Account newAccount = new Account
            {
                Info = new BasicAccountInfo()
                {
                    Username = Username,
                    DisplayName = DispName,
                    Coins = Coins,
                    Email = Email,
                    Verified = Verify
                }
            };

            if( PassHash != null )
            {
                newAccount.PrivInfo = new PrivateInfo()
                {
                    PassHash = PassHash
                };
            }

            if( bCardsSet )
            {
                newAccount.Cards = Cards;
            }
            if( bDecksSet )
            {
                newAccount.Decks = Decks;
            }

            // Now we need to serialize to disk
            writestart:
            try
            {
                var Serializer = new Amazon.Lambda.Serialization.Json.JsonSerializer();

                string SavedAccount = null;
                using( var Stream = new MemoryStream() )
                {
                    Serializer.Serialize( newAccount, Stream );
                    SavedAccount = Encoding.UTF8.GetString( Stream.ToArray() );
                }

                File.WriteAllText( FilePath, SavedAccount );
            }
            catch( Exception Ex )
            {
                Console.WriteLine( "=> Output failed to write! {0}", Ex );
                Console.WriteLine( "=> Enter 'y' to retry.. or 'n' to exit" );
                Console.Write( "> " );

                writeyn:
                string WriteYN = Console.ReadLine()?.Trim();

                if( String.IsNullOrEmpty( WriteYN ) )
                {
                    Console.WriteLine( "=> Invalid input.. enter 'y' to retry, or 'n' to quit" );
                    goto writeyn;
                }

                if( WriteYN.ToLower() == "y" )
                {
                    Console.WriteLine( "=> Retrying serialization/writing.." );
                    goto writestart;
                }
                else if( WriteYN.ToLower() == "n" )
                {
                    Console.WriteLine( "=> Exiting..." );
                    return false;
                }
                else
                {
                    Console.WriteLine( "=> Invalid input.. enter 'y' to retry or 'n' to quit" );
                    goto writeyn;
                }
            }

            Console.WriteLine( "=> Wrote generated account!" );
            Console.WriteLine( "=> Exiting account generator..." );
            Console.WriteLine( "" );

            return true;
        }


        static List< Card > RunCardEditor( int Max = 0 )
        {
            List<Card> Cards = new List<Card>();

            Console.WriteLine( "===> Card List Editor <===" );
            Console.WriteLine( "=> To stop entering cards.. type 'exit' at any point" );
            if( Max > 0 )
            {
                Console.WriteLine( "=> You can enter a maximum of {0} cards", Max );
            }

            while( true )
            {
                if( Max > 0 && CountCards( Cards ) >= Max )
                {
                    Console.WriteLine( "=> Maximum card count hit.. exiting editor" );
                    break;
                }

                Console.WriteLine( "" );
                Console.WriteLine( "Enter Card Id:" );
                Console.Write( "> " );

                string IdStr = Console.ReadLine()?.Trim();
                if( IdStr.ToLower() == "exit" )
                {
                    Console.WriteLine( "=> Exiting card editor" );
                    break;
                }
                if( String.IsNullOrWhiteSpace( IdStr ) || !UInt16.TryParse( IdStr, out UInt16 Id ) )
                {
                    Console.WriteLine( "=> Invalid card id entered!" );
                    continue;
                }

                Console.WriteLine( "Enter Card Count for '{0}' (or 'cancel' to not include this card):", Id );
                Console.Write( "> " );

                string CtStr = Console.ReadLine()?.Trim();
                if( CtStr.ToLower() == "exit" )
                {
                    Console.WriteLine( "=> Exiting card editor" );
                    break;
                }
                if( CtStr.ToLower() == "cancel" )
                {
                    Console.WriteLine( "=> Canceling current card.." );
                    continue;
                }
                if( String.IsNullOrWhiteSpace( CtStr ) || !UInt16.TryParse( CtStr, out UInt16 Count ) )
                {
                    Console.WriteLine( "=> Invalid count entered!" );
                    continue;
                }
                if( Cards.Exists( X => X.Id == Id ) )
                {
                    if( Count > 0 )
                    {
                        Cards.ForEach( X => { if( X.Id == Id ) X.Ct = Count; } );
                        Console.WriteLine( "=> Card with ID '{0}' already added.. updating count to '{1}'  {2} cards entered", Id, Count, CountCards( Cards ) );

                    }
                    else
                    {
                        Cards.RemoveAll( X => X.Id == Id );
                        Console.WriteLine( "=> Removing card '{0}' {1} cards entered", Id, CountCards( Cards ) );

                    }

                    continue;
                }
                else if( Count == 0 )
                {
                    Console.WriteLine( "=> Entered count was zero.. ignoring this card! {0} cards entered", CountCards( Cards ) );
                    continue;
                }

                Cards.Add( new Card()
                {
                    Id = Id,
                    Ct = Count
                } );

                Console.WriteLine( "=> Added new card! '{0}': {1}   {2} cards entered", Id, Count, CountCards( Cards ) );
            }

            return Cards;
        }

        static int CountCards( List< Card > Input )
        {
            int Output = 0;
            foreach( var C in Input )
                Output += C.Ct;

            return Output;
        }
    }
}
