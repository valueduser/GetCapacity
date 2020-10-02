# GetCapacity


An Azure Function to monitor an HTML page for JSON updates at a specific XPATH and store the results in blob storage.

To deploy to Azure:

1. Create Azure Service principle
2. Populate ./Deploy/config All parameters are required including:
  * subscription - Azure subscription ID
  * location - Azure region for the deployment (e.g. centralus)
  * appID - App ID for Service Principle
  * password - Password for Service Principle
  * tenant - Azure tenant ID
  * pathToFile - Path to a JSON formatted file in the form of TODO
  * functionProjectLocation - Location of source code on local disk
  * url - Target page
  * xpath - Location on page of expected JSON data
Run ./Deploy/deployInfra.sh
