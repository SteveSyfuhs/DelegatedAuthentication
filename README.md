# DelegatedAuthentication
Sample app showing delegated authentication

## Setup

### Domain Controller
You need a domain controller and it should probably have DNS configured. Domain should be `contoso.com`.

### App Host
You should run these apps on a separate application host. You should also set up DNS entries to map the SPNs below. Assuming an IP `10.0.0.3`:

 - service.contoso.com => 10.0.0.3
 - delegated.contoso.com => 10.0.0.3
 
 These should be A records and not CNAMEs.

### Service Accounts

#### App Service Account
 - Username: appserviceacct
 - Password: P@ssw0rd!
 - SPN (servicePrincipalName): host/service.contoso.com
 - delegate to (msDS-AllowedToDelegateTo): host/delegated.contoso.com
 
#### Delegate Service Account
 - Username: appdelegateacct
 - Password: P@ssw0rd!
 - SPN (servicePrincipalName): host/delegated.contoso.com
 
 ## Running the Apps
The service and delegated apps need to run as the service accounts created previously. This is most easily done using the `runas.exe` tool. 

You need to start the apps in the following order:
 
 1. `runas /profile /env /user:appserviceacct serverapp.exe`
 2. `runas /profile /env /user:appdelegateacct delegatedapp.exe`
 3. `clientapp.exe host.contoso.com 5555 delegated.contoso.com`
 
 Note that you may have to open the `5555` port on the firewall.
 
 ## App Behavior
The apps work in a `client => service => backend` relay. The client will use the current user's identity and authenticate to the service app. Once the service has authenticated the user, it will forward the original request to the delegated backend app using the current impersonated identity.
 
 You should see log messages in the console output indicating the various steps of the process. Note that the client is set up to wait 10 seconds and then constantly poll the server with messages.
 
 ## Debugging
 You can run all three apps locally without any parameters and it will route accordingly. This will inherently work every time because its the same user running each application so there's no security boundary being crossed. Note that the client is set up to delay for 10 seconds so the other two apps have time to spin up.
 
## Dependencies

 - https://github.com/antiduh/nsspi
