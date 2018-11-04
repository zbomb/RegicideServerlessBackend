using System;
using System.Collections.Generic;

namespace RegicideUtils
{
    struct Command
    {
        public string Name;
        public Func<string, bool> Callback;
    }

    class Program
    {
        static List<Command> Commands = new List<Command>();

        static void Init()
        {
            AccountGenerator.Init();
            BlockBuilder.Init();
        }

        static void Main( string[] args )
        {
            Console.WriteLine( "================== Regicide Utility Tool =====================" );
            Console.WriteLine( "==> Initializing..." );
            Init();

            Console.WriteLine( "==> Initialization Complete!\n" );
            Console.WriteLine( "Enter utility to run.." );

            while( true )
            {
                Console.Write( "> " );

                string Input = Console.ReadLine();
                string[] Exploded = Input.Split( ' ', 1, StringSplitOptions.RemoveEmptyEntries );
                if( Exploded.Length == 0 || String.IsNullOrWhiteSpace( Exploded[ 0 ] ) )
                    continue;

                Exploded[ 0 ] = Exploded[ 0 ].Trim().ToLower();

                if( Exploded[ 0 ] == "q" || Exploded[ 0 ] == "quit" )
                    break;

                // Lookup and run command
                bool bFound = false;
                foreach( var c in Commands )
                {
                    if( c.Name.ToLower() == Exploded[ 0 ] )
                    {
                        string Param = null;

                        if( Exploded.Length > 1 )
                            Param = Exploded[ 2 ];

                        c.Callback( Param );
                        bFound = true;
                        Console.WriteLine( "" );
                        Console.WriteLine( "========= Utility Complete '{0}' ==========", c.Name );
                        continue;
                    }
                }

                if( !bFound )
                    Console.WriteLine( "==> Command not found! '{0}'", Exploded[ 0 ] );
            }

            Console.WriteLine( "==> Exiting...." );
        }

        public static void AddCommand( Command inCommand )
        {
            if( Commands.Exists( X => X.Name.Equals( inCommand.Name, StringComparison.InvariantCultureIgnoreCase ) ) )
            {
                Console.WriteLine( "==> Init Error: Command with duplicate name detected.. '{0}'", inCommand.Name );
                return;
            }

            Commands.Add( inCommand );
        }
    }
}
