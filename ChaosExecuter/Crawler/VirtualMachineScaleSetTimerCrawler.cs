using AzureChaos.Entity;
using AzureChaos.Enums;
using AzureChaos.Helper;
using AzureChaos.Models;
using AzureChaos.Providers;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;

namespace ChaosExecuter.Crawler
{
    public static class VirtualMachineScaleSetTimerCrawler
    {
        private static readonly AzureClient AzureClient = new AzureClient();
        private static readonly IStorageAccountProvider StorageProvider = new StorageAccountProvider();

        [FunctionName("timercrawlerforvirtualmachinescaleset")]
        public static async void Run([TimerTrigger("0 */15 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            log.Info($"timercrawlerforvirtualmachinescaleset executed at: {DateTime.UtcNow}");

            var azureSettings = AzureClient.azureSettings;
            var resourceGroupList = ResourceGroupHelper.GetResourceGroupsInSubscription(AzureClient.azure, azureSettings);
            if (resourceGroupList == null)
            {
                log.Info($"timercrawlerforvirtualmachinescaleset: no resource groups to crawl");
                return;
            }

            var storageAccount = StorageProvider.CreateOrGetStorageAccount(AzureClient);
            foreach (var resourceGroup in resourceGroupList)
            {
                var scaleSetsList = await AzureClient.azure.VirtualMachineScaleSets.ListByResourceGroupAsync(resourceGroup.Name);
                var scaleSetVms = new List<IVirtualMachineScaleSetVMs>();
                VMScaleSetCrawlerResponseEntity vmScaletSetEntity = null;
                try
                {
                    TableBatchOperation scaleSetbatchOperation = new TableBatchOperation();
                    foreach (var scaleSet in scaleSetsList)
                    {
                        // insert the scale set details into table
                        vmScaletSetEntity = new VMScaleSetCrawlerResponseEntity(scaleSet.ResourceGroupName, scaleSet.Id.Replace("/", "!"));
                        vmScaletSetEntity.ResourceName = scaleSet.Name;
                        vmScaletSetEntity.ResourceType = scaleSet.Type;
                        vmScaletSetEntity.EntryInsertionTime = DateTime.UtcNow;
                        vmScaletSetEntity.ResourceGroupName = scaleSet.ResourceGroupName;
                        vmScaletSetEntity.RegionName = scaleSet.RegionName;
                        vmScaletSetEntity.Id = scaleSet.Id;
                        var virtualMachines = await scaleSet.VirtualMachines.ListAsync();
                        if(virtualMachines == null)
                        {
                            scaleSetbatchOperation.InsertOrReplace(vmScaletSetEntity);
                            continue;
                        }

                        vmScaletSetEntity.HasVirtualMachines = true;
                        scaleSetbatchOperation.InsertOrReplace(vmScaletSetEntity);

                        var partitionKey = scaleSet.Id.Replace('/', '!');
                        TableBatchOperation vmbatchOperation = new TableBatchOperation();
                        foreach (var instance in virtualMachines)
                        {
                            // insert the scale set vm instances to table
                            vmbatchOperation.InsertOrReplace(VirtualMachineHelper.ConvertToVirtualMachineEntity(instance, scaleSet.ResourceGroupName, scaleSet.Id, partitionKey, vmScaletSetEntity.AvailabilityZone, VirtualMachineGroup.ScaleSets.ToString()));
                        }

                        if (vmbatchOperation.Count > 0)
                        {
                            CloudTable vmTable = await StorageProvider.CreateOrGetTableAsync(storageAccount, azureSettings.VirtualMachineCrawlerTableName);
                            await vmTable.ExecuteBatchAsync(vmbatchOperation);
                        }
                    }

                    if (scaleSetbatchOperation.Count > 0)
                    {
                        CloudTable scaleSetTable = await StorageProvider.CreateOrGetTableAsync(storageAccount, azureSettings.ScaleSetCrawlerTableName);
                        await scaleSetTable.ExecuteBatchAsync(scaleSetbatchOperation);
                    }
                }
                catch (Exception ex)
                {
                    vmScaletSetEntity = new VMScaleSetCrawlerResponseEntity(azureSettings.Client.ResourceGroup, Guid.NewGuid().ToString())
                    {
                        Error = ex.Message
                    };
                    log.Error($"timercrawlerforvirtualmachinescaleset threw the exception ", ex, "timercrawlerforvirtualmachinescaleset");
                }
            }

        }

    }
}
