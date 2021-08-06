using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDeviceConfigurator
{
    public class Program
    {
        // En boolean der holder styr på aktiviteten.
        static bool mtAktiv;

        // Her laver vi en SerialPort class.
        static SerialPort ciscoPort;

        // Her laver vi vores Router Class, som kan indeholde flere værdier.
        public static class Router
        {
            public static string Output { get; set; }
        }

        public static void Main()
        {

            // Tråd der kan delegere arbejdet til en anden proceslinje, så vi kan have flere tråde igang på samme tid.
            Thread ciscoMT = new(Aflæsning);

            // Laver en string array, som ved brug af 'GetPortNames' funktionen, bliver fuldt med alle computerens tilgængelige porte.
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                int i = 0;
                Console.WriteLine($"\tIndex: {i} - {port}");
                i += 1;
            }

            // Parametre for Cisco konsoller: Portnavnet, 9600 baud, 8 data bits, ingen parity, 1 stop bit, og ingen flow control.
            if (ports.Length == 1)
            {
                ciscoPort = new SerialPort(ports[0], 9600, Parity.None, 8, StopBits.One);
            }
            else
            {
                ciscoPort = new SerialPort(ports[Convert.ToInt32(Console.ReadLine())], 9600, Parity.None, 8, StopBits.One);
            }

            // Her sætter vi read/write timeouts
            ciscoPort.ReadTimeout = 5000;
            ciscoPort.WriteTimeout = 5000;

            // Her tjekker vi at porten er åben, inden vi begynder at skrive til den.
            // Er den stadig lukket skriver programmet: "Fejl i åbning af porten." tilbage til brugeren.
            try
            {
                if (!(ciscoPort.IsOpen))
                {
                    ciscoPort.Open();
                    Console.WriteLine("\tPorten er åben.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\tFejl i åbning af porten: " + ex.Message);
            }

            Router.Output = "Empty";

            // Her opdaterer vi aktiviteten til vores boolean
            mtAktiv = true;
            // Her starter vi vores multithreading forløb, med vores sekundære thread.
            ciscoMT.Start();
            RouterRommon();

            // Her opdaterer vi aktiviteten til vores boolean
            mtAktiv = false;
            // Vi bruger .Join() til at smelte de 2 threads sammen, så der derefter kun er en thread.
            ciscoMT.Join();
            // Vi lukker for porten.
            ciscoPort.Close();
        }

        /// <summary>
        /// En funktion som konstant aflæser routerens output, og printer det til brugeren.
        /// </summary>
        public static void Aflæsning()
        {
            Console.WriteLine("\tAflæsning påbegyndt");
            while (mtAktiv)
            {
                // Denne try gør at programmet, forsøger at gøre det inde i loopet.
                // Hvis der er en fejl, så kan vi prøve at 'Catch' den.
                try
                {
                    Router.Output = ciscoPort.ReadLine();
                    Console.WriteLine(Router.Output);
                }
                // Vi catcher alle exceptions, dog tjekker vi om det er en timeoutexception eller ej.
                // Alle andre fejl skal dog stoppe programmet.
                catch (Exception ex)
                {
                    // Vi lander her hvis forbindelsen er timouted.
                    if (ex.GetType() == typeof(TimeoutException))
                    {
                        continue;
                    }
                    else
                    {

                        Console.WriteLine("\tFejl i sekundær thread: " + ex.Message);
                        Console.WriteLine("\tVil du lukke programmet? Yes[y] eller No[n]");
                        if (Console.ReadLine().ToLower().Contains("y"))
                        {
                            ciscoPort.Close();
                            Console.WriteLine("\tPort is closed and thread is closing.");
                            Thread.CurrentThread.Join();
                        }
                    }
                    
                }
            }
        }

        /// <summary>
        /// Dette program aflæser routerens output, og håndterer den således, så den bliver resettet.
        /// </summary>
        public static void RouterRommon()
        {
            bool routerReset = false;

            int i = 0;

            Console.WriteLine("\tPlease powercycle the router.");
            while (!routerReset)
            {
                Console.WriteLine($"\tCycle: {i}");
                bool rommonWait = true;
                while (rommonWait)
                {
                    try
                    {
                        if (Router.Output.Contains("Readonly ROMMON"))
                        {
                            Console.WriteLine("\tRommon detected"); 
                            // Send byte værdien for CTRL+Break
                            bool breakSpam = true;
                            do
                            {
                                try
                                {
                                    // The application spams the representation of break to the router here.
                                    //ciscoPort.Write(new byte[] {  }, 0, 1);
                                    ciscoPort.Write(new byte[] { SendKeys.Send("^+Oem6") }, 0, 1);
                                    
                                    if (Router.Output.Contains("user interrupt"))
                                    {
                                        Console.WriteLine("\tBreak/Pause was successful");
                                        breakSpam = false;
                                    }
                                } catch (TimeoutException)
                                {
                                    Console.WriteLine("\tTB");
                                }
                            } while (breakSpam);
                        }

                        if (Router.Output.Contains("rommon"))
                        {
                            Console.WriteLine("\tEntered Rommon");
                            rommonWait = false;
                        }
                    }
                    catch (TimeoutException)
                    {
                        Console.Write("\tT1");
                    }
                }


                ciscoPort.WriteLine("confreg 0x2142");
                ciscoPort.WriteLine("reset");

                bool startWait = true;

                while (startWait)
                {
                    try
                    {
                        if (Router.Output.Contains("Would you"))
                        {
                            ciscoPort.WriteLine("No");
                            if (Router.Output.Contains("terminate"))
                            {
                                ciscoPort.WriteLine("Yes");
                            }
                            startWait = false;
                        }
                    }
                    catch (TimeoutException)
                    {
                        Console.Write("\tT2");
                    }
                }


                bool waitForOpen = true;


                while (waitForOpen)
                {
                    try
                    {
                        if (Router.Output.Contains("Router"))
                        {
                            waitForOpen = false;
                        }
                    }
                    catch (TimeoutException)
                    {
                        Console.Write("\tT3");
                    }
                }


                ciscoPort.Write(new byte[] { 13 }, 0, 1);
                ciscoPort.WriteLine("enable");
                ciscoPort.WriteLine("config t");
                ciscoPort.WriteLine("config-register 0x2102");
                ciscoPort.WriteLine("do copy run start\r");
                ciscoPort.WriteLine("do reload");
                if (Router.Output.Contains("save"))
                {
                    ciscoPort.WriteLine("No");
                }
                i++;
                routerReset = true;
                Console.WriteLine("\tFinished");
            }
        }
    }
}