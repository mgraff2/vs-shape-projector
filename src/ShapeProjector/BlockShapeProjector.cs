using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ShapeProjector
{
    /// <summary>
    /// The projector block. Right-click opens the configuration dialog on the client (spec §2, §8 step 3).
    /// Interaction pattern copied from vanilla BlockTicker — api-notes.md §h.3 (BlockTicker.cs:8-16).
    /// Base type: Vintagestory.API.Common.Block — api-notes §c.1.
    /// </summary>
    public class BlockShapeProjector : Block
    {
        // public virtual bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        // — api-notes §c.1 (Block.cs:1480). Runs on the client first; returning false stops the interaction
        // and it is not synced to the server (doc at Block.cs:1479).
        //
        // ORDER MATTERS (step-3 bug): the base implementation returns FALSE unless a BlockBehavior handled
        // the interaction — `if (flag2) return flag; return false;` (Block.cs:1503-1507) — so calling base
        // first and bailing on false never reached the dialog. Vanilla BlockTicker calls its BE first and
        // returns true, delegating to base only when the BE declines (BlockTicker.cs:8-16); same here.
        // On the client Claims.TryAccess always returns true (ILandClaimAPI.cs:23), so the real permission
        // check is the server-side one in BEShapeProjector.OnReceivedClientPacket (api-notes §h.5).
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // BlockEntity GetBlockEntity(BlockPos position) — IBlockAccessor.cs:324.
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEShapeProjector be)
            {
                // IWorldAccessor.Side — IWorldAccessor.cs:107; EnumAppSide.Client = 2 (EnumAppSide.cs:19).
                // Dialogs exist only on the client (BlockEntityTicker.OnInteract checks the same, api-notes §h.3).
                if (world.Side == EnumAppSide.Client)
                {
                    be.OpenDialog();
                }
                return true;
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        // ------------------------------------------------------------------ placement rule (user ruling on Q2)
        // "Any solid surface" = a solid block BENEATH only — no walls/ceilings.
        // public virtual bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        // — Block.cs:968 (called by the base TryPlaceBlock after behaviors, Block.cs:955-959; base checks
        // replaceability, entity intersection and claims first). The ground test is vanilla
        // BlockRequireSolidGround.HasSolidGround: block below's UP side must be solid —
        // blockAccessor.GetBlock(pos.Down(1)).SideIsSolid(blockAccessor, pos, BlockFacing.UP.Index)
        // (BlockRequireSolidGround.cs:17-22; here with the non-mutating BlockPos.DownCopy, BlockPos.cs:452;
        // SideIsSolid(IBlockAccessor, BlockPos, int) — Block.cs:599). failureCode "requiresolidground" is the
        // vanilla code (BlockBehaviorUnstableFalling.cs:105); the client then shows
        // Lang.Get("placefailure-requiresolidground") = "Cannot place this block here. Requires a solid ground"
        // (SystemMouseInWorldInteractions.cs:442; ASSETS/game/lang/en.json:5509).
        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

            BlockPos below = blockSel.Position.DownCopy();
            if (!world.BlockAccessor.GetBlock(below).SideIsSolid(world.BlockAccessor, below, BlockFacing.UP.Index))
            {
                failureCode = "requiresolidground";
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ config-carrying drops (spec §2, §8 step 6; api-notes §i)

        // public virtual ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) — Block.cs:1362 (default:
        // new ItemStack(this), Block.cs:1390 — for the emissive variant that would yield projector-on).
        // Creative middle-click and drops must give the -off block regardless of variant (spec §7); the JSON
        // "drops" already do, this covers the C# pick path. Pattern: BlockBed.OnPickBlock returns a fixed
        // variant (BlockBed.cs:156-160); RegistryObject.CodeWithVariant(string, string) — RegistryObject.cs:121;
        // IBlockAccessor.GetBlock(AssetLocation) — IBlockAccessor.cs:297.
        // The BE's configuration rides on the stack — vanilla pattern BlockShapeFromAttributes.OnPickBlock
        // (stack.Attributes.SetString from the BE, BlockShapeFromAttributes.cs:453-466; api-notes §i.2); here
        // the whole parameter tree is attached via the ITreeAttribute indexer (ITreeAttribute.cs:17).
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            Block off = world.BlockAccessor.GetBlock(CodeWithVariant("state", "off")) ?? this;
            ItemStack stack = new ItemStack(off);
            // BlockEntity GetBlockEntity(BlockPos position) — IBlockAccessor.cs:324. Inside GetDrops/OnPickBlock
            // the BE still exists: SpawnDropsAndRemoveBlock removes the block only after the drops are
            // computed (Block.cs:1140-1173; api-notes §i.1).
            if (world.BlockAccessor.GetBlockEntity(pos) is BEShapeProjector be)
            {
                stack.Attributes[BEShapeProjector.StackConfigKey] = be.BuildConfigTree();
            }
            return stack;
        }

        // public virtual ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
        // — Block.cs:1308. The drop IS the configured pick-block stack (spec §2 "drops an item that retains
        // its configuration"); attributes survive the engine's drop Clone (ItemStack.Clone → GetEmptyClone
        // copies Attributes.Clone(), ItemStack.cs:411-435; api-notes §i.1).
        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            return new ItemStack[] { OnPickBlock(world, pos) };
        }

        // public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        // — Block overrides CollectibleObject's at Block.cs:2402 (virtual base CollectibleObject.cs:1871);
        // ItemSlot.Itemstack — ItemSlot.cs:51; ITreeAttribute.GetTreeAttribute returns null when absent
        // (ITreeAttribute.cs:242). One-line summary so a configured item is distinguishable (this task's §1).
        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            Vintagestory.API.Datastructures.ITreeAttribute? cfg = inSlot.Itemstack?.Attributes?.GetTreeAttribute(BEShapeProjector.StackConfigKey);
            if (cfg != null)
            {
                ProjectorParams p = ProjectorParams.FromTree(cfg);
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                dsc.AppendLine(Vintagestory.API.Config.Lang.Get("shapeprojector:info-item-summary",
                    p.Layers.Count, p.Dx.ToString("0.#", ci), p.Dz.ToString("0.#", ci)));
            }
        }

        // public virtual WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        // — Block.cs:2215. WorldInteraction { ActionLangCode (WorldInteraction.cs:32), MouseButton (WorldInteraction.cs:18) };
        // EnumMouseButton.Right = 2 (EnumMouseButton.cs:11); construction as vanilla Block.cs:2254-2259.
        // The lang key "shapeprojector:blockhelp-projector-configure" is the Curator's (lang/en.json).
        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "shapeprojector:blockhelp-projector-configure",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}
