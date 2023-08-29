Write-Host "Installing Azure modules for Powershell"
Install-Module Az
Import-Module Az

Connect-AzAccount

$deployment = "GradeSyncDeployment"
$apiArtifactPath = "infrastructure/artifacts/api-deploy.zip"
$workerArtifactPath = "infrastructure/artifacts/worker-deploy.zip"

$subscription = Read-Host "Input the subscription to deploy in"
Set-AzContext -Subscription $subscription

$rgDefault = 'rg-ms-grade-sync'
$rg = Read-Host "Input the resource group to deploy in. Press enter to accept the default [$($rgDefault)]"
$rg = ($rgDefault, $rg)[[bool]$rg]

$locationDefault = 'eastus2'
$location = Read-Host "Input the location to deploy in. Press enter to accept the default [$($locationDefault)]"
$location = ($locationDefault, $location)[[bool]$location]

Write-Host "subscription '$subscription' resource group '$rg' location '$location'" 

$shouldDeployInfrastructure = Read-Host "Do you want to deploy the infrastructure? (y/n)" 
if ($shouldDeployInfrastructure -eq 'y') {
  Get-AzResourceGroup -Name $rg -ErrorVariable notPresent -ErrorAction SilentlyContinue

  if ($notPresent) {
    Write-Host "Creating resource group '$rg' in location '$location'" 
    New-AzResourceGroup -Name $rg -Location $location
  }
  else {
    Write-Host "Resource group '$rg' in location '$location' already exists, skipping" 
  }

  Write-Host "Deploying resources to resource group '$rg' in subscription '$subscription'" 
  New-AzResourceGroupDeployment -Name $deployment -ResourceGroupName $rg -TemplateFile 'infrastructure/azuredeploy.json' -TemplateParameterFile 'infrastructure/azuredeploy.parameters.json'
}
elseif ($shouldDeployInfrastructure -eq 'n') {
  Write-Host "Skipping infrastructure deployment due to user input" 
}
else {
  throw "Invalid choice for infrastructure deployment"
}

$shouldCreateBuildArtifacts = Read-Host "Do you want to create build artifacts? (y/n)" 
if ($shouldCreateBuildArtifacts -eq 'y') {
  Write-Host "Creating build artifacts"

  dotnet publish -c Release -r linux-x64 --sc -v q 
  (cd grade-sync-api/bin/Release/net7.0/linux-x64/publish && zip -r "$OLDPWD/$apiArtifactPath" .)
  (cd grade-sync-worker/bin/Release/net7.0/linux-x64/publish && zip -r "$OLDPWD/$workerArtifactPath" .)
}
elseif ($shouldCreateBuildArtifacts -eq 'n') {
  Write-Host "Skipping build artifact deployment due to user input" 
}
else {
  throw "Invalid choice for build artifact deployment"
}

$shouldDeployBuildArtifacts = Read-Host "Do you want to deploy the build artifacts? (y/n)" 
if ($shouldDeployBuildArtifacts -eq 'y') {
  $dep = Get-AzResourceGroupDeployment -Name $deployment -ResourceGroupName $rg
  $api = $dep.Parameters["appservice_api_name"].Value
  $worker = $dep.Parameters["appservice_worker_name"].Value

  Write-Host "Deploying build artifacts to '$api'"
  az webapp deployment source config-zip --src $apiArtifactPath -n $api -g $rg

  Write-Host "Deploying build artifacts to '$worker'"
  az functionapp deployment source config-zip --src $workerArtifactPath -n $worker -g $rg
}
elseif ($shouldDeployBuildArtifacts -eq 'n') {
  Write-Host "Skipping build artifact deployment due to user input" 
}
else {
  throw "Invalid choice for build artifact deployment"
}

