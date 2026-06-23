using InstancedLoot.Enums;
using InstancedLoot.Hooks;

namespace InstancedLoot.ObjectHandlers;

public class PrinterHandler : AbstractObjectHandler
{
    public override string[] HandledObjectTypes { get; } =
    {
        ObjectType.Duplicator, ObjectType.DuplicatorLarge, ObjectType.DuplicatorWild, ObjectType.DuplicatorMilitary,
        ObjectType.LunarCauldronWhiteToGreen, ObjectType.LunarCauldronGreenToRed, ObjectType.LunarCauldronRedToWhite,
        ObjectType.ShrineCleanse
    };

    public override ObjectInstanceMode ObjectInstanceMode => ObjectInstanceMode.None;

    public override void Init(ObjectHandlerManager manager)
    {
        base.Init(manager);
        
        // Printers no longer drop their output through ShopTerminalBehavior; the game runs the
        // EntityStates.Duplicator.Duplicating state instead. DuplicatorHandler instances that drop.
        // ShopTerminalBehaviorHandler is still registered for the other PaidWithItem objects
        // (cauldrons / cleansing pool) that may continue to use ShopTerminalBehavior.
        Plugin.HookManager.RegisterHandler<DuplicatorHandler>();
        Plugin.HookManager.RegisterHandler<ShopTerminalBehaviorHandler>();
        Plugin.HookManager.RegisterHandler<PurchaseInteractionHandler>();
        Plugin.HookManager.RegisterHandler<HackingBeaconHandler>();
    }
}