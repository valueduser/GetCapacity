#!/bin/bash

CONFIGFILE="./config"

eval $(sed '/:/!d;/^ *#/d;s/:/ /;' < "$CONFIGFILE" | while read -r key val
do
    str="$key='$val'"
    echo "$str"
done)

dt=$(date '+%Y%m%d%H%M');

resourceGroupName="capacity"$dt
storageAccountName="blob"$dt
fnStorageAccountName="fnsto"$dt
functionAppName="getcapacity"$dt
functionContainer="function"$dt
keyvaultName="kv"$dt
blobName="blob"$dt
containerName="container"$dt
servicePrincipalName="sp"$dt

echo "Creating Azure resources..."

az login --service-principal -u $appID --password $password --tenant $tenant

az account set --subscription $subscription

echo "Creating resource group" $resourceGroupName"..."
az group create --name $resourceGroupName --location $location

echo "Creating key vault" $keyvaultName"..."
az keyvault create \
  --resource-group $resourceGroupName \
  --name $keyvaultName \
  --location $location \
  --sku "standard" \
  --subscription $subscription

echo "Creating storage account" $storageAccountName"..."
az storage account create  \
  --name $storageAccountName  \
  --resource-group $resourceGroupName \
  --access-tier "Cool" \
  --kind "BlobStorage" \
  --location $location \
  --sku "Standard_LRS" \
  --subscription $subscription

echo "Retrieving connection string..."
connectionString=`az storage account show-connection-string --name $storageAccountName --resource-group $resourceGroupName --output tsv`

echo "Populating key vault..."
az keyvault secret set \
  --vault-name $keyvaultName \
  --name "Url" \
  --value $url

az keyvault secret set \
  --vault-name $keyvaultName \
  --name "XPath" \
  --value $xpath

az keyvault secret set \
  --vault-name $keyvaultName \
  --name "BlobContainerName" \
  --value $containerName

az keyvault secret set \
  --vault-name $keyvaultName \
  --name "StorageAccountName" \
  --value $blobName

az keyvault secret set \
  --vault-name $keyvaultName \
  --name "ResourceGroupName" \
  --value $resourceGroupName

az keyvault secret set \
  --vault-name $keyvaultName \
  --name "BlobConnectionString" \
  --value $connectionString

az keyvault secret set \
  --vault-name $keyvaultName \
  --name "ContainerName" \
  --value $containerName

echo "Creating storage container" $containerName"..."
az storage container create \
  --name $containerName \
  --public-access off \
  --connection-string $connectionString

echo "Uploading to blob storage" $blobName"..."
az storage blob upload --container-name $containerName \
  --file $pathToFile"data.json" \
  --connection-string $connectionString \
  --name "data.json"

echo "Creating function app" $functionAppName
az storage account create \
  --name $fnStorageAccountName \
  --resource-group $resourceGroupName \
  --location $location \
  --sku Standard_LRS \
  --subscription $subscription

az functionapp create \
  --name $functionAppName  \
  --storage-account $fnStorageAccountName  \
  --consumption-plan-location $location \
  --functions-version 2 \
  --resource-group $resourceGroupName 

echo "Building and publishing the function project" $functionProjectLocation"/GetCapacity.csproj..."
dotnet build $functionProjectLocation --configuration Release
cd $functionProjectLocation"/bin/Release/netcoreapp3.1/"
zip -r $functionAppName.zip * 

az storage container create \
  --name $functionContainer \
  --public-access off \
  --connection-string $connectionString

az storage container create \
  --name $functionContainer \
  --public-access off \
  --connection-string $connectionString

echo "Uploading function zip to blob"
az storage blob upload --container-name $functionContainer \
  --file $functionAppName.zip \
  --connection-string $connectionString \
  --name $functionAppName.zip

format='+%Y-%m-%dT%H:%M:%SZ'
start=`date -v-24H ${format}`
end=`date -v+8760H ${format}`

sas=`az storage account generate-sas \
  --permissions rl \
  --connection-string $connectionString \
  --expiry $end \
  --start $start \
  --resource-types sco \
  --services b \
  --output tsv`
sas="${sas//%3A/:}"
  
echo "Cleaning up "${functionAppName}".zip..."
rm ${functionAppName}.zip

packageLocation="https://$storageAccountName.blob.core.windows.net/$functionContainer/$functionAppName.zip?$sas"

az functionapp config appsettings set \
  --name $functionAppName \
  --resource-group $resourceGroupName \
  --settings AzureKeyVault:ClientId=$appID AzureKeyVault:ClientSecret=$password AzureKeyVault:VaultName=$keyvaultName WEBSITE_RUN_FROM_PACKAGE=$packageLocation

echo "Done!"
