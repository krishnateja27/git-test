using AzureChaos;
using AzureChaos.Entity;
using AzureChaos.Models;
using AzureChaos.Providers;
using ChaosExecuter.Helper;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace ChaosExecuter.Crawler
{
    public static class VirtualMachinesCrawler
    {
        private static ADConfiguration config = new ADConfiguration();
        private static StorageAccountProvider storageProvider = new StorageAccountProvider(config);
        private static CloudTableClient tableClient = storageProvider.tableClient;
        private static CloudTable table = storageProvider.CreateOrGetTable("VirtualMachineTable");


        [FunctionName("VirtualMachinesCrawler")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = "crawlvirtualmachines")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("VirtualMachinesCrawler function processed a request.");
            try
            {
                TableBatchOperation batchOperation = new TableBatchOperation();
                var azure_client = AzureClient.GetAzure(config);
                var loadBalancersVms = await GetVmsFromLoadBalancers(azure_client);
                // will be listing out only the standalone virtual machines.
                var pagedCollection = await azure_client.VirtualMachines.ListByResourceGroupAsync(config.ResourceGroup);
                if(pagedCollection == null || !pagedCollection.Any())
                {
                    return req.CreateResponse(HttpStatusCode.NoContent);
                }

                var virtualMachines = pagedCollection.Select(x => x).Where(x => string.IsNullOrWhiteSpace(x.AvailabilitySetId) &&
                !loadBalancersVms.Contains(x.Id, StringComparer.OrdinalIgnoreCase));
                foreach (var virtualMachine in virtualMachines)
                {
                    batchOperation.Insert(VirtualMachineHelper.ConvertToVirtualMachineEntity(virtualMachine));
                }

                await Task.Factory.StartNew(() =>
                {
                    table.ExecuteBatch(batchOperation);
                });
            }
            catch (Exception ex)
            {
                log.Error($"VirtualMachines Crawler function Throw the exception ", ex, "VirtualMachinesCrawler");
                return req.CreateResponse(HttpStatusCode.InternalServerError, ex.Message);
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }

        /// <summary>Get the list of </summary>
        /// <param name="azure_client"></param>
        /// <returns>Returns the list of vm ids which are in the load balancers.</returns>
        private static async Task<List<string>> GetVmsFromLoadBalancers(IAzure azure_client)
        {
            var vmIds = new List<string>();
            var pagedCollection = await azure_client.LoadBalancers.ListByResourceGroupAsync(config.ResourceGroup);
            if(pagedCollection == null)
            {
                return vmIds;
            }

            var loadBalancers = pagedCollection.Select(x => x);
            if (loadBalancers == null || !loadBalancers.Any())
            {
                return vmIds;
            }

            vmIds.AddRange(loadBalancers.SelectMany(x => x.Backends).SelectMany(x => x.Value.GetVirtualMachineIds()));
            return vmIds;
        }
    }
}
