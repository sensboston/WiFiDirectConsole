using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.UI.ViewManagement;

namespace WiFiDirectConsole
{
    class Program
    {
        static bool doWork = true;
        static readonly string CLRF = (Console.IsOutputRedirected) ? "" : "\r\n";
        static string versionInfo;
        static int exitCode = 0;
        static ManualResetEvent notifyCompleteEvent = null;
        static ManualResetEvent delayEvent = null;

        static List<DeviceInformation> deviceInfos = new List<DeviceInformation>();
        static Dictionary<string, IList<WiFiDirectInformationElement>> elements = new Dictionary<string, IList<WiFiDirectInformationElement>>();
        static WiFiDirectDevice wfdDevice = null;

        static DeviceWatcher deviceWatcher;

        // Internal variables for loop and conditional operators implementation
        static List<string> forEachCommands = new List<string>();
        static List<string> forEachDeviceNames = new List<string>();
        static int forEachCmdCounter = 0;
        static int forEachDeviceCounter = 0;
        static bool forEachCollection = false;
        static bool forEachExecution = false;
        static string forEachDeviceMask = "";
        static int inIfBlock = 0;
        static bool failedConditional = false;
        static bool closingIfBlock = false;

        static short? GOI = null;

        static TimeSpan _timeout = TimeSpan.FromSeconds(3);

        // OUI assigned to the Wi-Fi Alliance.
        public static readonly byte[] WfaOui = { 0x50, 0x6F, 0x9A };

        // OUI assigned to Microsoft Corporation.
        public static readonly byte[] MsftOui = { 0x00, 0x50, 0xF2 };

        public static readonly string strServerPort = "50001";
        public static readonly int iAdvertisementStartTimeout = 5000; // in ms

        static void Main(string[] args)
        {
            // Get app name and version
            var name = Assembly.GetCallingAssembly().GetName();
            versionInfo = string.Format($"{name.Name} ver. {name.Version.Major:0}.{name.Version.Minor:0}.{name.Version.Build:0}\n");
            if (!Console.IsInputRedirected) Console.WriteLine(versionInfo);

            // Set Ctrl+Break/Ctrl+C handler
            Console.CancelKeyPress += Console_CancelKeyPress;

            // Run main loop
            MainAsync(args).Wait();

            // Return exit code to the shell
            // For scripting/batch processing, it's an ERRORLEVEL cmd.exe shell variable
            Environment.Exit(exitCode);
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            // If we're waiting for async results, let's abandon the wait
            if (notifyCompleteEvent != null)
            {
                notifyCompleteEvent.Set();
                notifyCompleteEvent = null;
                e.Cancel = true;
            }
            // If we're waiting for "delay" command, let's abandon the wait
            else if (delayEvent != null)
            {
                delayEvent.Set();
                delayEvent = null;
                e.Cancel = true;
            }
            // Otherwise, quit the app
            else
            {
                if (!Console.IsInputRedirected)
                    Console.WriteLine("\nWiFiDirectConsole is terminated");
                e.Cancel = false;
                doWork = false;
            }
        }

        static async Task MainAsync(string[] args)
        {
            // Start endless WiFi Direct device watcher
            deviceWatcher = DeviceInformation.CreateWatcher(WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint), 
                new string[] { "System.Devices.WiFiDirect.IsMiracastLcpSupported" });

            deviceWatcher.Added += (DeviceWatcher sender, DeviceInformation devInfo) =>
            {
                if (!deviceInfos.Any(d => d.Id.Equals(devInfo.Id)))
                    deviceInfos.Add(devInfo);
            };

            deviceWatcher.Removed += (DeviceWatcher sender, DeviceInformationUpdate devInfo) =>
            {
                var dev = deviceInfos.FirstOrDefault(d => d.Id.Equals(devInfo.Id));
                if (dev != null) deviceInfos.Remove(dev);
            };

            deviceWatcher.Updated += (_, __) => { };
            deviceWatcher.EnumerationCompleted += (DeviceWatcher sender, object arg) => { sender.Stop(); };
            deviceWatcher.Stopped += (DeviceWatcher sender, object arg) => { sender.Start(); };
            deviceWatcher.Start();

            // Initialize internal variables
            string cmd = string.Empty;
            bool skipPrompt = false;

            // Main loop
            while (doWork)
            {
                if (!Console.IsInputRedirected && !skipPrompt)
                    Console.Write("WiFiDirect: ");

                skipPrompt = false;

                try
                {
                    var userInput = string.Empty;

                    // If we're inside "foreach" loop, process saved commands
                    if (forEachExecution)
                    {
                        userInput = forEachCommands[forEachCmdCounter];
                        if (forEachCmdCounter++ >= forEachCommands.Count - 1)
                        {
                            forEachCmdCounter = 0;
                            if (forEachDeviceCounter++ > forEachDeviceNames.Count - 1)
                            {
                                forEachExecution = false;
                                forEachCommands.Clear();
                                userInput = string.Empty;
                                skipPrompt = true;
                            }
                        }
                    }
                    // Otherwise read the stdin
                    else userInput = Console.ReadLine();

                    // Check for the end of input
                    if (Console.IsInputRedirected && string.IsNullOrEmpty(userInput))
                    {
                        doWork = false;
                    }
                    else userInput = userInput?.TrimStart(new char[] { ' ', '\t' });

                    if (!string.IsNullOrEmpty(userInput))
                    {
                        string[] strs = userInput.Split(' ');
                        cmd = strs.First().ToLower();
                        string parameters = string.Join(" ", strs.Skip(1));

                        if (forEachCollection && !cmd.Equals("endfor"))
                        {
                            forEachCommands.Add(userInput);
                        }
                        if (cmd == "endif" || cmd == "elif" || cmd == "else")
                            closingIfBlock = false;
                        else
                        {
                            if ((inIfBlock > 0 && !closingIfBlock) || inIfBlock == 0)
                            {
                                await HandleSwitch(cmd, parameters);
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    Console.WriteLine(error.Message);
                }
            }
            deviceWatcher.Stop();
        }

        static async Task HandleSwitch(string cmd, string parameters)
        {
            switch (cmd)
            {
                case "if":
                    inIfBlock++;
                    exitCode = 0;
                    if (parameters != "")
                    {
                        string[] str = parameters.Split(' ');
                        await HandleSwitch(str[0], str.Skip(1).Aggregate((i, j) => i + " " + j));
                        closingIfBlock = (exitCode > 0);
                        failedConditional = closingIfBlock;
                    }
                    break;

                case "elif":
                    if (failedConditional)
                    {
                        exitCode = 0;
                        if (parameters != "")
                        {
                            string[] str = parameters.Split(' ');
                            await HandleSwitch(str[0], str.Skip(1).Aggregate((i, j) => i + " " + j));
                            closingIfBlock = (exitCode > 0);
                            failedConditional = closingIfBlock;
                        }
                    }
                    else
                        closingIfBlock = true;
                    break;

                case "else":
                    if (failedConditional)
                    {
                        exitCode = 0;
                        if (parameters != "")
                        {
                            string[] str = parameters.Split(' ');
                            await HandleSwitch(str[0], str.Skip(1).Aggregate((i, j) => i + " " + j));
                        }
                    }
                    else
                        closingIfBlock = true;
                    break;

                case "endif":
                    if (inIfBlock > 0)
                        inIfBlock--;
                    failedConditional = false;
                    break;

                case "foreach":
                    forEachCollection = true;
                    forEachDeviceMask = parameters.ToLower();
                    break;

                case "endfor":
                    if (string.IsNullOrEmpty(forEachDeviceMask))
                        forEachDeviceNames = deviceInfos.OrderBy(d => d.Name).Where(d => !string.IsNullOrEmpty(d.Name)).Select(d => d.Name).ToList();
                    else
                        forEachDeviceNames = deviceInfos.OrderBy(d => d.Name).Where(d => d.Name.ToLower().StartsWith(forEachDeviceMask)).Select(d => d.Name).ToList();
                    forEachDeviceCounter = 0;
                    forEachCmdCounter = 0;
                    forEachCollection = false;
                    forEachExecution = (forEachCommands.Count > 0);
                    break;

                case "q":
                case "quit":
                case "exit":
                    doWork = false;
                    break;

                case "cls":
                case "clr":
                case "clear":
                    Console.Clear();
                    break;

                case "?":
                case "help":
                    Help();
                    break;

                case "delay":
                    Delay(parameters);
                    break;

                case "ls":
                case "list":
                    ListDevices(parameters);
                    break;

                case "i":
                case "info":
                    PrintDeviceElements(parameters);
                    break;

                case "o":
                case "open":
                case "pair":
                case "connect":
                    if (forEachExecution && forEachDeviceCounter > 0)
                        parameters = parameters.Replace("$", forEachDeviceNames[forEachDeviceCounter - 1]);

                    exitCode += await ConnectToDevice(parameters);
                    break;

                case "opc":
                case "openpc":
                case "pairpc":
                case "connectpc":
                    if (forEachExecution && forEachDeviceCounter > 0)
                        parameters = parameters.Replace("$", forEachDeviceNames[forEachDeviceCounter - 1]);

                    exitCode += await ConnectToPC(parameters);
                    break;


                case "c":
                case "close":
                case "unpair":
                case "disconnect":
                    if (forEachExecution && forEachDeviceCounter > 0)
                        parameters = parameters.Replace("$", forEachDeviceNames[forEachDeviceCounter - 1]);

                    exitCode += await UnpairDevice(parameters);
                    break;

                case "set":
                    SetParam(parameters);
                    break;

                default:
                    Console.WriteLine("Unknown command. Type \"?\" for help.");
                    break;
            }
        }

        /// <summary>
        /// Displays app version and available commands
        /// </summary>
        static void Help()
        {
            Console.WriteLine(versionInfo +
                "\n  help, ?\t\t\t: show help information\n" +
                "  quit, q\t\t\t: quit from application\n" +
                "  list, ls [w]\t\t\t: show available WiFi Direct devices\n" +
                "  info <name> or <#>\t\t: show available device elements\n" +
                "  delay <msec>\t\t\t: pause execution for a certain number of milliseconds\n" +
                "  set goi=[0..15]\t\t: set GroupOwnerIntent value. Default value is 14\n" +
                "  connect <name> or <#>\t\t: connect to WiFi Direct device. Syn: o, open, pair\n" +
                "  connectpc <name> or <#>\t: connect to Windows 10 PC with enabled projection. Syn: opc, openpc, pairpc\n" +
                "  disconnect\t\t\t: disconnect from currently connected device. Syn: c, close, unpair\n" +
                "  foreach [device_mask]\t\t: starts devices enumerating loop\n" +
                "  endfor\t\t\t: end foreach loop\n" +
                "  if <cmd> <params>\t\t: start conditional block dependent on function returning w\\o error\n" +
                "    elif\t\t\t: another conditionals block\n" +
                "    else\t\t\t: if condition == false block\n" +
                "  endif\t\t\t\t: end conditional block\n\n"
                );
        }

        static void Delay(string param)
        {
            if (uint.TryParse(param, out uint milliseconds))
            {
                delayEvent = new ManualResetEvent(false);
                delayEvent.WaitOne((int)milliseconds, true);
                delayEvent = null;
            }
        }

        static void SetParam(string param)
        {
            // Remove spaces
            param = param.Replace(" ", "");
            if (param.StartsWith("goi"))
            {
                if (param.Equals("goi=null")) GOI = null;
                else
                {
                    string[] values = param.Split('=');
                    if (values.Length == 2 && short.TryParse(values[1], out short newGOI))
                        if (newGOI >= 0 && newGOI < 16) GOI = newGOI;
                }
                string goi = GOI.HasValue ? GOI.ToString() : "null";
                Console.WriteLine($"Group owner intent set to {goi}");
                return;
            }
            Console.WriteLine("Error setting parameter. Use: set goi=14");
        }

        /// <summary>
        /// List available WiFi Direct devices
        /// </summary>
        /// <param name="param">optional, 'w' means "wide list"</param>
        static void ListDevices(string param)
        {
            var names = deviceInfos.OrderBy(d => d.Name).Where(d => !string.IsNullOrEmpty(d.Name)).Select(d => d.Name).ToList();
            if (string.IsNullOrEmpty(param))
            {
                for (int i = 0; i < names.Count(); i++)
                    Console.WriteLine($"#{i:00}: {names[i]}");
            }
            else if (param.Replace("/", "").ToLower().Equals("w"))
            {
                if (names.Count > 0)
                {
                    // New formatting algorithm for "wide" output; we should avoid tabulations and use spaces only
                    int maxWidth = names.Max(n => n.Length);
                    int columns = Console.WindowWidth / (maxWidth + 5);
                    List<string>[] strColumn = new List<string>[columns];

                    for (int i = 0; i < names.Count; i++)
                    {
                        if (strColumn[i % columns] == null) strColumn[i % columns] = new List<string>();
                        strColumn[i % columns].Add(string.Format("#{0:00}: {1}   ", i, names[i]));
                    }

                    int maxNumColumns = Math.Min(columns, strColumn.Count(l => l != null));

                    for (int i = 0; i < maxNumColumns; i++)
                    {
                        int max = strColumn[i].Max(n => n.Length);
                        for (int j = 0; j < strColumn[i].Count; j++)
                            strColumn[i][j] += new string(' ', max - strColumn[i][j].Length);
                    }

                    for (int j = 0; j < strColumn[0].Count; j++)
                    {
                        string s = "";
                        for (int i = 0; i < maxNumColumns; i++)
                            if (j < strColumn[i].Count) s += strColumn[i][j];
                        Console.WriteLine(s.TrimEnd());
                    }
                }
            }
        }

        /// <summary>
        /// Print WiFi Direct device information elements
        /// Please note: Windows 10 PCs (with enabled projection) usually have 5 elements,
        /// standalone Miracast dongles only 3
        /// </summary>
        /// <param name="deviceName"></param>
        static void PrintDeviceElements(string deviceName)
        {
            var discoveredDevice = Utils.GetDeviceInformationByNameOrNumber(deviceInfos.OrderBy(d => d.Name).ToList(), deviceName);

            if (discoveredDevice != null)
            {
                try
                {
                    if (!elements.ContainsKey(discoveredDevice.Id))
                        elements[discoveredDevice.Id] = WiFiDirectInformationElement.CreateFromDeviceInformation(discoveredDevice);

                    var informationElements = elements[discoveredDevice.Id];
                    if (informationElements != null)
                    {
                        StringWriter message = new StringWriter();

                        foreach (WiFiDirectInformationElement informationElement in informationElements)
                        {
                            string ouiName = CryptographicBuffer.EncodeToHexString(informationElement.Oui);
                            string value = string.Empty;
                            Byte[] bOui = informationElement.Oui.ToArray();

                            if (bOui.SequenceEqual(MsftOui))
                            {
                                // The format of Microsoft information elements is documented here:
                                // https://msdn.microsoft.com/en-us/library/dn392651.aspx
                                // with errata here:
                                // https://msdn.microsoft.com/en-us/library/mt242386.aspx
                                ouiName += " (Microsoft)";
                            }
                            else if (bOui.SequenceEqual(WfaOui))
                            {
                                ouiName += " (WFA)";
                            }
                            message.WriteLine($"OUI {ouiName}, Type {informationElement.OuiType} {value}");
                        }

                        message.Write($"Information elements found: {informationElements.Count}");
                        Console.WriteLine(message.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("No Information element found: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Pair with WiFi Direct device
        /// </summary>
        /// <param name="pairing"></param>
        /// <returns></returns>
        static async Task<bool> RequestPairDeviceAsync(DeviceInformationPairing pairing)
        {
            // This is a custom pairing procedure
            // SS note: I've never tried PIN-enabled devices
            void CustomPairing_PairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
            {
                using (Deferral deferral = args.GetDeferral())
                {
                    switch (args.PairingKind)
                    {
                        case DevicePairingKinds.DisplayPin:
                            Console.WriteLine($"Enter this PIN on the remote device: {args.Pin}");
                            args.Accept();
                            break;

                        case DevicePairingKinds.ConfirmOnly:
                            args.Accept();
                            break;

                        case DevicePairingKinds.ProvidePin:
                            Console.Write($"Enter PIN:");
                            var pin = Console.ReadLine();
                            args.Accept(pin);
                            break;
                    }
                }
            }

            // PreferredPairingProcedure always should be GroupOwnerNegotiation
            WiFiDirectConnectionParameters connectionParams = new WiFiDirectConnectionParameters()
            {
                PreferredPairingProcedure = WiFiDirectPairingProcedure.GroupOwnerNegotiation,
            };

            // Group owner intent is a "tricky" parameter: default value 14 works fine with "ScreenBeam Mini2" wireless receiver
            // but not working with Sharp SmartTV "AnyCast", should be set to 0 or 1
            if (GOI != null) connectionParams.GroupOwnerIntent = (short) GOI;

            DevicePairingKinds devicePairingKinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin;

            DeviceInformationCustomPairing customPairing = pairing.Custom;
            customPairing.PairingRequested += CustomPairing_PairingRequested;

            DevicePairingResult result = await customPairing.PairAsync(devicePairingKinds, DevicePairingProtectionLevel.Default, connectionParams);
            if (result.Status != DevicePairingResultStatus.Paired)
            {
                Console.WriteLine($"PairAsync failed, Status: {result.Status}");
                return false;
            }
            return true;
        }

        static async Task<int> ConnectToDevice(string deviceName)
        {
            var discoveredDevice = Utils.GetDeviceInformationByNameOrNumber(deviceInfos.OrderBy(d => d.Name).ToList(), deviceName);

            if (discoveredDevice != null)
            {
                // First, unpair from any connected device
                var devs = deviceInfos.ToList();
                try
                {
                    foreach (var d in devs)
                        await UnpairDevice(d.Name, false);
                }
                catch { }

                Console.WriteLine($"Connecting to {discoveredDevice.Name}...");

                if (!discoveredDevice.Pairing.IsPaired)
                {
                    if (!await RequestPairDeviceAsync(discoveredDevice.Pairing))
                    {
                        return -1;
                    }
                }

                try
                {
                    // IMPORTANT: FromIdAsync needs to be called from the UI thread
                    wfdDevice = await WiFiDirectDevice.FromIdAsync(discoveredDevice.Id);

                    //IReadOnlyList<EndpointPair> endpointPairs = wfdDevice.GetConnectionEndpointPairs();
                    //HostName remoteHostName = endpointPairs[0].RemoteHostName;

                    Console.WriteLine($"Connected to {discoveredDevice.Name}");
                    return 0;
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("FromIdAsync was canceled by user");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Connect operation threw an exception: {ex.Message}");
                }
            }
            return -1;
        }

        static async Task<int> ConnectToPC(string deviceName)
        {
            var discoveredDevice = Utils.GetDeviceInformationByNameOrNumber(deviceInfos.OrderBy(d => d.Name).ToList(), deviceName);

            if (discoveredDevice != null)
            {
                // First, unpair from any connected device
                var devs = deviceInfos.ToList();
                try
                {
                    foreach (var d in devs)
                        await UnpairDevice(d.Name, false);
                }
                catch { }

                Console.WriteLine($"Connecting to {discoveredDevice.Name} via ProjectionManager...");

                // For some strange reasons, StartProjectingAsync can't be awaited in the console thread
                try { ProjectionManager.StartProjectingAsync(-1, -1, discoveredDevice); } catch { }

                await Task.Delay(1000);

                try
                {
                    // IMPORTANT: FromIdAsync needs to be called from the UI thread
                    wfdDevice = await WiFiDirectDevice.FromIdAsync(discoveredDevice.Id);
                    return 0;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            return 1;
        }


        /// <summary>
        /// Unpair from WiFi Direct device
        /// </summary>
        /// <param name="deviceName"></param>
        /// <param name="outputResult"></param>
        /// <returns></returns>
        static async Task<int> UnpairDevice(string deviceName, bool outputResult = true)
        {
            var discoveredDevice = Utils.GetDeviceInformationByNameOrNumber(deviceInfos.OrderBy(d => d.Name).ToList(), deviceName);

            if (discoveredDevice != null)
            {
                if (outputResult)
                    Console.WriteLine($"Unpairing from {discoveredDevice.Name}...");

                DeviceUnpairingResult result = await discoveredDevice.Pairing.UnpairAsync();

                if (outputResult) Console.WriteLine($"{result.Status}");

                if (wfdDevice != null)
                {
                    wfdDevice.Dispose();
                    wfdDevice = null;
                }

                return (result.Status == DeviceUnpairingResultStatus.Failed) ? 1 : 0;
            }
            return 1;
        }
    }
}