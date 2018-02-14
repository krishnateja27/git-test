﻿using AzureChaos.Core.Entity;
using AzureChaos.Core.Enums;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AzureChaos.Core.Helper
{
    public static class VirtualMachineHelper
    {
        /// <summary>Convert the Virtual machine to virtual machine crawler response entity.</summary>
        /// <param name="virtualMachine">The virtual machine.</param>
        /// <param name="partitionKey">The partition key for the virtaul machine entity.</param>
        /// <param name="vmGroup">Vm group name.</param>
        /// <returns></returns>
        public static VirtualMachineCrawlerResponse ConvertToVirtualMachineEntity(IVirtualMachine virtualMachine, string partitionKey, string vmGroup = "")
        {
            var virtualMachineCrawlerResponseEntity = new VirtualMachineCrawlerResponse(partitionKey, virtualMachine.Id.Replace("/", "!"))
            {
                EntryInsertionTime = DateTime.UtcNow,
                RegionName = virtualMachine.RegionName,
                ResourceGroupName = virtualMachine.ResourceGroupName,
                ResourceName = virtualMachine.Name,
                AvailableSetId = virtualMachine.AvailabilitySetId,
                UpdateDomain = virtualMachine.InstanceView?.PlatformUpdateDomain,
                FaultDomain =  virtualMachine.InstanceView?.PlatformFaultDomain,
                ResourceType = virtualMachine.Type,
                Id = virtualMachine.Id,
                AvailabilityZone = virtualMachine.AvailabilityZones.Count > 0 ?
                    int.Parse(virtualMachine.AvailabilityZones.FirstOrDefault().Value) : 0,
                VirtualMachineGroup = string.IsNullOrWhiteSpace(vmGroup) ? VirtualMachineGroup.VirtualMachines.ToString() : vmGroup,
                State = virtualMachine.PowerState.Value
            };

            return virtualMachineCrawlerResponseEntity;
        }

        /// <summary>Convert the Virtual machine to virtual machine crawler response entity.</summary>
        /// <param name="scaleSetVm">The virtual machine.</param>
        /// <param name="resourceGroup">The resource group name.</param>
        /// <param name="scaleSetId">Scale set name of the vm.</param>
        /// <param name="partitionKey">The partition key for the virtaul machine entity.</param>
        /// <param name="availabilityZone">The availability zone value for the virtual machine scale set vm instance</param>
        /// <param name="vmGroup">Virtual machine group name.</param>
        /// <returns></returns>
        public static VirtualMachineCrawlerResponse ConvertToVirtualMachineEntity(IVirtualMachineScaleSetVM scaleSetVm, string resourceGroup, string scaleSetId, 
            string partitionKey, int? availabilityZone, string vmGroup = "")
        {
            var virtualMachineCrawlerResponseEntity = new VirtualMachineCrawlerResponse(partitionKey, scaleSetVm.Id.Replace("/", "!"))
            {
                EntryInsertionTime = DateTime.UtcNow,
                RegionName = scaleSetVm.RegionName,
                ResourceGroupName = resourceGroup,
                ResourceName = scaleSetVm.Name,
                ResourceType = scaleSetVm.Type,
                ScaleSetId = scaleSetId,
                Id = scaleSetVm.Id,
                AvailabilityZone = availabilityZone != 0 ? availabilityZone : null,
                VirtualMachineGroup = string.IsNullOrWhiteSpace(vmGroup) ? VirtualMachineGroup.VirtualMachines.ToString() : vmGroup,
                State = scaleSetVm.PowerState.Value
            };

            return virtualMachineCrawlerResponseEntity;
        }

        /// <summary>Create the table batch operation for the scheduled entity for the set of virtual machines.</summary>
        /// <param name="filteredVmSet">Set of virtual machines.</param>
        /// <param name="schedulerFrequency">Schedule frequency, it will be reading from the config</param>
        /// <param name="virtualMachineGroup"></param>
        /// <returns></returns>
        public static TableBatchOperation CreateScheduleEntity(IList<VirtualMachineCrawlerResponse> filteredVmSet, int schedulerFrequency, VirtualMachineGroup virtualMachineGroup)
        {
            TableBatchOperation tableBatchOperation = new TableBatchOperation();
            Random random = new Random();
            DateTime randomExecutionDateTime = DateTime.UtcNow.AddMinutes(random.Next(1, schedulerFrequency));
            var sessionId = Guid.NewGuid().ToString();
            foreach (var item in filteredVmSet)
            {
                if (item == null)
                {
                    continue;
                }

                var actionType = GetAction(item.State);
                if (actionType == ActionType.Unknown)
                {
                    continue;
                }

                var entityEntry = RuleEngineHelper.ConvertToScheduledRuleEntity(item, sessionId, actionType, randomExecutionDateTime, virtualMachineGroup);
                if (entityEntry != null)
                {
                    tableBatchOperation.InsertOrMerge(entityEntry);
                }
            }

            return tableBatchOperation;
        }

        public static TableBatchOperation CreateScheduleEntityForAvailabilityZone(IList<VirtualMachineCrawlerResponse> filteredVmSet, int schedulerFrequency)
        {
            var tableBatchOperation = new TableBatchOperation();
            var random = new Random();
            var randomExecutionDateTime = DateTime.UtcNow.AddMinutes(random.Next(1, schedulerFrequency));
            var sessionId = Guid.NewGuid().ToString();
            foreach (var item in filteredVmSet)
            {
                var actionType = GetAction(item.State);
                if (actionType == ActionType.Unknown)
                {
                    continue;
                }

                tableBatchOperation.InsertOrMerge(RuleEngineHelper.ConvertToScheduledRuleEntityForAvailabilityZone(item, sessionId, actionType, randomExecutionDateTime));
            }

            return tableBatchOperation;
        }
        public static TableBatchOperation CreateScheduleEntityForAvailabilitySet(IList<VirtualMachineCrawlerResponse> filteredVmSet, int schedulerFrequency, bool domainFlage)
        {
            var tableBatchOperation = new TableBatchOperation();
            var random = new Random();
            var randomExecutionDateTime = DateTime.UtcNow.AddMinutes(random.Next(1, schedulerFrequency));
            var sessionId = Guid.NewGuid().ToString();
            foreach (var item in filteredVmSet)
            {
                var actionType = GetAction(item.State);
                if (actionType == ActionType.Unknown)
                {
                    continue;
                }

                tableBatchOperation.InsertOrMerge(RuleEngineHelper.ConvertToScheduledRuleEntityForAvailabilitySet(item, sessionId, actionType, randomExecutionDateTime, domainFlage));
            }

            return tableBatchOperation;
        }
        /// <summary>Get the action based on the current state of the virtual machine.</summary>
        /// <param name="state">Current state of the virtual machine.</param>
        /// <returns></returns>
        public static ActionType GetAction(string state)
        {
            var powerState = PowerState.Parse(state);
            if (powerState == PowerState.Running || powerState == PowerState.Starting)
            {
                return ActionType.PowerOff;
            }

            if (powerState == PowerState.Stopping || powerState == PowerState.Stopped)
            {
                return ActionType.Start;
            }

            return ActionType.Unknown;
        }
    }
}
