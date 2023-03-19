# WiFiDirectConsole
Console application that allows you to connect to WiFi direct/Miracast enabled devices to cast PC screen (Smart TVs, FireSticks, Windows PCs etc.)

## Requrements:
- OS Windows 10, Windows 11
- WiFi direct compatible network adapter

You can use WiFiDirecConsole for automation from the standard Windows console ("Command prompt") in the interactive mode or in the batch mode (in the .bat, .cmd files)

## Commands:
  - **help, ?**                       : show help information
  - **quit, q**                       : quit from application
  - **list, ls [w]**                  : show available WiFi Direct devices
  - **info <name> or <#>**            : show available device elements
  - **delay <msec>**                  : pause execution for a certain number of milliseconds
  - **set goi=[0..15]**               : set GroupOwnerIntent value. Default value is 14
  - **connect <name> or <#>**         : connect to WiFi Direct device. Syn: o, open, pair
  - **connectpc <name> or <#>**       : connect to Windows 10 PC with enabled projection. Syn: opc, openpc, pairpc
  - **disconnect <name> or <#>**      : disconnect from currently connected device. Syn: c, close, unpair
  - **foreach [device_mask]**         : starts devices enumerating loop
  - **endfor**                        : end foreach loop
  - **if <cmd> <params>**             : start conditional block dependent on function returning w\o error
    - **elif**                        : another conditionals block
    - **else**                        : if condition == false block
  - **endif**                         : end conditional block

Note: I haven't tested this application on Windows 11; if you able to test, please do and let me know at the "Issues". Thank you!
