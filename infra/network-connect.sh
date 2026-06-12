#!/usr/bin/env bash
#
# network-connect.sh — establish VM1 <-> VM2 private connectivity for the BMT MongoDB-on-VM
# backend. Realized commands that were required so the load generator (VM1) can reach mongod
# on VM2:27017 over private IP.
#
#   1. NSG rule on VM2's subnet NSG allowing inbound TCP 27017 from VM1's subnet (10.2.0.0/24).
#   2. Bidirectional VNet peering between VM1's VNet and VM2's VNet.
#
# These complement the host firewall rule created by install-mongo.ps1 (which allows 27017
# on the VM2 host itself). The NSG here is the network-layer allow; both are required.
#
# No credentials are embedded. Pass the subscription via $AZ_SUBSCRIPTION_ID (or `az account set`).
# Idempotent-ish: re-running NSG/peering create on existing resources is a no-op error you can ignore.
#
# Usage:
#   export AZ_SUBSCRIPTION_ID="<your-subscription-id>"
#   ./network-connect.sh

set -euo pipefail

RG="rg-db-test-hpc"
SUB="${AZ_SUBSCRIPTION_ID:?set AZ_SUBSCRIPTION_ID to your subscription id}"

VM1_VNET="vm-dbtest-hpc-0-vnet"
VM2_VNET="vm-dbtest-hpc-1-mongo-vnet"
VM2_NSG="vm-dbtest-hpc-1-mongo-vnet-default-nsg-koreacentral"

VM1_SUBNET_PREFIX="10.2.0.0/24"   # VM1 (load generator) subnet — source allowed to reach Mongo
MONGO_PORT="27017"

VM1_VNET_ID="/subscriptions/${SUB}/resourceGroups/${RG}/providers/Microsoft.Network/virtualNetworks/${VM1_VNET}"
VM2_VNET_ID="/subscriptions/${SUB}/resourceGroups/${RG}/providers/Microsoft.Network/virtualNetworks/${VM2_VNET}"

# 1. NSG: allow Mongo wire port from VM1's subnet into VM2.
az network nsg rule create \
  --resource-group "${RG}" \
  --nsg-name "${VM2_NSG}" \
  --name allow-mongo-from-vm1 \
  --priority 310 \
  --direction Inbound --access Allow --protocol Tcp \
  --source-address-prefixes "${VM1_SUBNET_PREFIX}" \
  --destination-port-ranges "${MONGO_PORT}"

# 2. VNet peering (must be created on both sides).
az network vnet peering create \
  -g "${RG}" -n vm1-to-vm2 \
  --vnet-name "${VM1_VNET}" \
  --remote-vnet "${VM2_VNET_ID}" \
  --allow-vnet-access

az network vnet peering create \
  -g "${RG}" -n vm2-to-vm1 \
  --vnet-name "${VM2_VNET}" \
  --remote-vnet "${VM1_VNET_ID}" \
  --allow-vnet-access

echo "Connectivity configured: NSG allow 27017 from ${VM1_SUBNET_PREFIX} + VM1<->VM2 peering."
