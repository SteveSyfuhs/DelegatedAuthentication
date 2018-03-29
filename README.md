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
 
### App Host Permissions
The application needs to run with the `SeImpersonatePrivilege` privilege. Normally you host this sort of process as a Windows Service (e.g. via IIS), which inherits this permission from the service control. In this case you need to assign this privilege since this sample scenario doesn't run as a service. You can do this a few ways.

 - Add the `contoso\hostserviceacct` account to the local administrators group. Don't do that.
 - Assign the privilege via Local Security Policy. Use the "Impersonate a Client After Authentication" policy and add the `contoso\hostserviceacct` account.
 - Apply via group policy. Maybe. This means this account would have this permission on every host, and that's bad.
 - Run it as a service. This is preferred in the long run.
 
You'll note that running this sample indicates the delegated app receives an identification token. This means the app cannot then further delegate to other apps via 3rd or 4th hop. You can also include the `contoso\appdelegateacct` in the `SeImpersonatePrivilege` for further effect.
 
## Running the Apps
The service and delegated apps need to run as the service accounts created previously. This is most easily done using the [PSExec](https://docs.microsoft.com/en-us/sysinternals/downloads/psexec) tool. As it turns out, the`runas` tool is not good to use in this case because it tends to uss restricted tokens, which complicates troubleshooting.

You need to start the apps in the following order:
 
 1. `PsExec.exe -u contoso\hostserviceacct -p P@ssw0rd! -h -i -d c:\sample\serverapp.exe`
 2. `PsExec.exe -u contoso\appdelegateacct -p P@ssw0rd! -h -i -d c:\sample\delegatedapp.exe`
 3. `clientapp.exe host.contoso.com 5555 delegated.contoso.com`
 
 Note that you may have to open the `5555` and `5655` ports on the firewall.
 
 ## App Behavior
The apps work in a `client => service => backend` relay. The client will use the current user's identity and authenticate to the service app. Once the service has authenticated the user, it will forward the original request to the delegated backend app using the current impersonated identity.
 
 You should see log messages in the console output indicating the various steps of the process. Note that the client is set up to wait 10 seconds and then constantly poll the server with messages.
 
The communications mechanism is a modified implementation found in the [NSSPI](https://github.com/antiduh/nsspi) sample. Don't use it as a basis for delegated communications. It's ...wonky.

 ## Debugging
 You can run all three apps locally without any parameters and it will route accordingly. This will inherently work every time because its the same user running each application so there's no security boundary being crossed. Note that the client is set up to delay for 10 seconds so the other two apps have time to spin up.
 
## Dependencies

 - https://github.com/antiduh/nsspi
