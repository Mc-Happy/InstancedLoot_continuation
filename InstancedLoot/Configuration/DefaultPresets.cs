using System.Collections.Generic;
using InstancedLoot.Enums;

namespace InstancedLoot.Configuration;

public static class DefaultPresets
{
    public static Dictionary<string, ConfigPreset> Presets = new()
    {
        { "None", new ConfigPreset("Do not instance anything, vanilla behavior", new Dictionary<string, InstanceMode>()) },
        {
            "Default", new ConfigPreset("Instance most things, tries to be a sensible default.", new Dictionary<string, InstanceMode>
            {
                {ObjectAlias.Chests, InstanceMode.InstancePreferred},
                {ObjectAlias.Shops, InstanceMode.InstancePreferred},
                {ObjectAlias.Shrines, InstanceMode.InstancePreferred},
                {ObjectAlias.Equipment, InstanceMode.InstanceObject},
                {ObjectAlias.ItemSpawned, InstanceMode.InstanceObjectForOwnerOnly},
                {ObjectType.LunarChest, InstanceMode.InstancePreferred},
                {ObjectType.VoidTriple, InstanceMode.InstancePreferred},
                {ObjectType.Sacrifice, InstanceMode.InstanceItems},
                {ObjectType.HuntersTricorn, InstanceMode.InstanceItems},
                {ObjectType.TeleporterBoss, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.BossGroup, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.SuperRoboBallEncounter, InstanceMode.InstanceItemForOwnerOnly},
            })
        },
        {
            "Selfish", new ConfigPreset("Instance things for owner where applicable. Avoids increasing total item/interactible count.", new Dictionary<string, InstanceMode>
            {
                {ObjectAlias.Chests, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectAlias.Shops, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectAlias.Shrines, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectAlias.Equipment, InstanceMode.InstanceObject},
                {ObjectAlias.Void, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectAlias.ItemSpawned, InstanceMode.InstanceBothForOwnerOnly},
                {ObjectAlias.PaidWithItem, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectAlias.Printers, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectAlias.Cauldrons, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.Scrapper, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.LunarChest, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.TeleporterBoss, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.BossGroup, InstanceMode.InstanceItemForOwnerOnly},
                {ObjectType.SuperRoboBallEncounter, InstanceMode.InstanceItemForOwnerOnly},
            })
        },
        { "EVERYTHING", new EverythingConfigPreset() }
    };

    public class EverythingConfigPreset : ConfigPreset
    {
        public EverythingConfigPreset()
        {
            Description = "Instance absolutely everything. Not recommended.";
        }
        
        public override InstanceMode GetConfigForName(string name)
        {
            return InstanceMode.InstancePreferred;
        }
    }
}