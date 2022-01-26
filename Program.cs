using System;
using System.Threading;
using EasyModbus;
using System.IO;

/* FROM FDC EZT570SV COMMS MANUAL: The program parameters are a separate group of registers that are used for sending ramp/soak programs to the EZT-570S.
The manner in which the program steps are sent to the EZT-570S is specific and must be followed exactly.
Each program step consists of 15 data registers.  Programs must be written one step at a time,
using a multiple write command(0x10) to write the data for all 15 registers at once.This allows
programs to be stored as two - dimensional arrays, of which code can be written to simply index
through the array step - by - step, and transmit the program to the EZT - 570S.
The first 15 registers of the Program contain specific settings related to the program.These include AutoStart
settings, the program name, the length of the program(number of steps), and guaranteed soak band settings.
These values are always transmitted as the first “step” of the program.

INSTRUCTIONS;
CONSOLE APPLICATION ONLY, WILL LOOK AT C:/EZTPROFILES to find list of profiles available to load
PROMPT USER TO SELECT PROFILE FROM THOSE FOUND
PROMPT USER TO ENTER IP ADDRESS OF EZT570SV
WILL LOAD EACH STEP OF PROFILE ON AT A TIME AND WAIT 1SEC IN BETWEEN STEPS
PROFILE WILL NOT BE ON EZT SD CARD BUT ONLY IN CPU MEMORY
PRESS ANY KEY TO EXIT

TODO:
EXCEPTION CATCHING FOR NO PROFILES, FAILED CONNECTION TO CLIENT
RUN CHECK ON PROFILE TO CONFIRM IT IS VALID
ALLOW MULTIPLE UNITS TO BE LOADED CONSECUTIVELY BY INPUTING SEVERAL IPS
ALLOW FOR AUTOMATIC RUNNING OF JUST LOADED PROFILE
HANDLE DECODING OF HEADER AND OUTPUT TO CONSOLE FOR VERIFACTION, GS LIMITS, PROFILE NAME, ETC
EZT INT HANDLING CORRECTION HAPPENS AS A INT FUNCTION ON THE HEADER AND STRING OPERATION IN THE STEPS, PICK ONE
*/
namespace ModBusTest
{
    class Program
    {
        public static void Main(string[] args)
        {
            //set to true to run without modbus client interactions
            bool isDebugging = false;
            Console.WriteLine("***EZT 570Sv MODBUS Profile Loader v0.2***");

            //Ask user to select profile from list of found and parse in Profile Object
            var _profile = ProfileParse(SelectProfile());
            //Load Client with Selected and Parsed Profile from Above
            LoadProfile(_profile);

            //read stream and parse profile into appropriate header and steps int arrays
            Profile ProfileParse(string profileToParse)
            {
                //Read First Line and Find Number of steps in profile by looking at 10th value of first line
                StreamReader sr = new StreamReader(profileToParse);
                var firstLine = sr.ReadLine();
                var headerString = firstLine.Split(',');
                var numSteps = Convert.ToInt32(headerString[9]);
                int[] header = new int[15];
                Console.WriteLine("Number of steps detected in profile: {0}", numSteps);

                //convert string array to int array
                for (int i = 0; i < headerString.Length; i++)
                {
                    header[i] = Convert.ToInt32(headerString[i]);

                    /*EZTVIEW OUTPUT IRREGULARITY WORKAROUND - 
                        EZVIEW OMITS DECIMALS IF THEY ARE ZERO BUT INCLUDES THEM IF THEY ARE !0,
                        THS WORKAROUND HANDLES MULTIPLYING THE SP BY 10 IF NO DECIMAL IS PRESENT,
                        IF A DECIMAL IS PRESENT IT REMOVES IT
                        THE EZT IS EXPECTING A DESIRED SP OF 24 TO  BE SENT AS 240 
                        THIS IS ONLY ACTIVE FOR VALUES 10,11,12,13,14 (LOOP SP1,2,3,4,5)
                        */

                    if (i >= 10)
                    {
                        header[i] = header[i] * 10;
                    }
                }

                //Initiate 2 dimension array 15 x number of steps
                int[,] steps = new int[numSteps, 15];

                //Read number of lines for number of steps

                for (var stepNum = 0; stepNum < numSteps; stepNum++)
                {
                    var currentLine = sr.ReadLine();
                    var currentValues = currentLine.Split(',');
                    for (int i = 0; i < currentValues.Length; i++)
                    {
                        /*EZTVIEW OUTPUT IRREGULARITY WORKAROUND - 
                        EZVIEW OMITS DECIMALS IF THEY ARE ZERO BUT INCLUDES THEM IF THEY ARE !0,
                        THS WORKAROUND HANDLES MULTIPLYING THE SP BY 10 IF NO DECIMAL IS PRESENT,
                        IF A DECIMAL IS PRESENT IT REMOVES IT
                        THE EZT IS EXPECTING A DESIRED SP OF 24 TO  BE SENT AS 240 
                        THIS IS ONLY ACTIVE FOR VALUES 10,11,12,13,14 (LOOP SP1,2,3,4,5)
                        */
                        if (i >= 10)
                        {
                            //If value has a decimal point, remove it - this is a workaround to the way EZTView treats SP's with decimals
                            if (currentValues[i].Contains("."))
                            {
                                var split = currentValues[i].Split('.');
                                currentValues[i] = split[0] + split[1];
                            }
                            //if value doesn't have a decimal add a zero to the end - multiplies by 10 to match what the EZT is expecting(SP of 24 should be sent as 240)
                            else
                            {
                                currentValues[i] = currentValues[i] + "0";
                            }
                        }
                    }
                    
                    for (var key = 0; key < 15; key++)
                    {
                        steps[stepNum, key] = Convert.ToInt32(currentValues[key]);
                        if (isDebugging) { Console.WriteLine(steps[stepNum, key]); }
                    }
                }
                return new Profile(header, steps);
            }

            //take parsed profile and load profile onto client
            void LoadProfile(Profile profileToLoad)
            {
                var header = profileToLoad.Header;
                var steps = profileToLoad.Steps;

                //SETTINGS
                int waitTime = 1000;
                int controllerPort = 502;

                // CONNECT TO GIVEN CONTROLLER IP
                Console.WriteLine("Enter Unit IP Address to load profile and press enter...");
                string controllerIP = Console.ReadLine();
                ModbusClient modbusClient = new ModbusClient(controllerIP, controllerPort);
                Console.WriteLine("Attemping to Connect to EZT client at {0}:{1}...", controllerIP, controllerPort);
                if (!isDebugging) { modbusClient.Connect(); }
                Thread.Sleep(waitTime);

                //WRITE FIRST 15 HEADER REGISTERS FROM GIVE PROFILE TEXT FILES FIRST LINE
                //HEADER STARTS AT 200 AND ENDS AT 214
                //WRITE STEPS CONSECUTIVELY 15 REGISTERS AT A TIME, 99MAX, 
                //FIRST STEP START REGISTER IS 215 END RGISTER IS 229
                //INDEXED BY 15 REGISTERS PER STEPS, ONLY WRITE STEPS THAT ARE RUN

                //Write Header Data to Controller
                Console.WriteLine("Writing headervalue to controller");
                if (!isDebugging) { modbusClient.WriteMultipleRegisters(200, header); }
                
                Thread.Sleep(waitTime);

                //Write Step Data to Controller Consecutively
                for (int currentStep = 0; currentStep < steps.GetLength(0); currentStep++)
                {
                    int[] step = new int[15];
                    for (int  currentValue = 0; currentValue < step.Length; currentValue++)
                    {
                        step[currentValue] = steps[currentStep, currentValue];
                    }

                    Console.WriteLine("Sending step {0}...", currentStep + 1);
                    if (!isDebugging) { modbusClient.WriteMultipleRegisters(215 + currentStep * 15, step); }
                    Console.WriteLine("Step {0} sent waiting {1} milliseconds", currentStep + 1, waitTime);
                    Thread.Sleep(waitTime);
                }

                if (!isDebugging) { modbusClient.Disconnect(); }
                Console.Write("Complete press any key to exit");
                Console.ReadKey();
            }
            //find profiles and ask user to select from list displayed
            string SelectProfile()
            {
                Console.WriteLine("Searching for profiles at C:/EZTPROFILES....");
                Console.WriteLine("Enter number for desired profile to use...");
                var files = Directory.GetFiles("C:/EZTPROFILES");
                var i = 1;
                foreach (var file in files)
                {
                    Console.WriteLine(i.ToString() + "...." + file);
                    i++;
                }
                var selection = Console.ReadLine();
                return files[Convert.ToInt32(selection) - 1];
            }
        }
    }
}
