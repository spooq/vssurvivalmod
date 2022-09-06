﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{

    public class BEBehaviorBurning : BlockEntityBehavior
    {
        public float startDuration;
        public float remainingBurnDuration;

        Block fireBlock;
        Block fuelBlock;
        string startedByPlayerUid;

        ILoadedSound ambientSound;
        static Cuboidf fireCuboid = new Cuboidf(-0.125f, 0, -0.125f, 1.125f, 1, 1.125f);
        WeatherSystemBase wsys;
        Vec3d tmpPos = new Vec3d();


        public float TimePassed
        {
            get { return startDuration - remainingBurnDuration; }
        }

        public Action<float> OnFireTick;
        public Action<bool> OnFireDeath;
        public ActionBoolReturn ShouldBurn;
        public ActionBoolReturn<BlockPos> OnCanBurn;


        public bool IsBurning;

        public BlockPos FirePos;
        public BlockPos FuelPos;
        long l1, l2;


        public BEBehaviorBurning(BlockEntity be) : base(be) {

            OnCanBurn = (pos) =>
            {
                Block block = Api.World.BlockAccessor.GetBlock(pos);
                return block?.CombustibleProps != null && block.CombustibleProps.BurnDuration > 0;
            };
            ShouldBurn = () => true;
            OnFireTick = (dt) =>
            {
                if (remainingBurnDuration <= 0)
                {
                    KillFire(true);
                }
            };

            OnFireDeath = (consumefuel) =>
            {
                if (consumefuel)
                {
                    var becontainer = Api.World.BlockAccessor.GetBlockEntity(FuelPos) as BlockEntityContainer;
                    becontainer?.OnBlockBroken();

                    Api.World.BlockAccessor.SetBlock(0, FuelPos);
                    Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(FuelPos);
                    TrySpreadTo(FuelPos);
                }

                Api.World.BlockAccessor.SetBlock(0, FirePos);

                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(FirePos);
            };
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            fireBlock = Api.World.GetBlock(new AssetLocation("fire"));
            if (fireBlock == null) fireBlock = new Block();

            if (IsBurning)
            {
                initSoundsAndTicking();
            }
        }

        public void OnFirePlaced(BlockFacing fromFacing, string startedByPlayerUid)
        {
            OnFirePlaced(Blockentity.Pos, Blockentity.Pos.AddCopy(fromFacing.Opposite), startedByPlayerUid);
        }

        public void OnFirePlaced(BlockPos firePos, BlockPos fuelPos, string startedByPlayerUid)
        {
            if (IsBurning || !ShouldBurn()) return;

            this.startedByPlayerUid = startedByPlayerUid;

            FirePos = firePos.Copy();
            FuelPos = fuelPos.Copy();
            
            if (FuelPos == null || !canBurn(FuelPos))
            {
                foreach (BlockFacing facing in BlockFacing.ALLFACES)
                {
                    BlockPos npos = FirePos.AddCopy(facing);
                    fuelBlock = Api.World.BlockAccessor.GetBlock(npos);
                    if (canBurn(npos))
                    {
                        FuelPos = npos;
                        startDuration = remainingBurnDuration = fuelBlock.CombustibleProps.BurnDuration;
                        return;
                    }
                }

                startDuration = 1;
                remainingBurnDuration = 1;
                FuelPos = FirePos.Copy(); // No fuel left
            }
            else
            {
                fuelBlock = Api.World.BlockAccessor.GetBlock(FuelPos);

                if (fuelBlock.CombustibleProps != null)
                {
                    startDuration = remainingBurnDuration = fuelBlock.CombustibleProps.BurnDuration;
                }
            }

            startBurning();
        }



        private void startBurning()
        {
            if (IsBurning) return;
            IsBurning = true;
            if (Api != null) initSoundsAndTicking();
        }

        BlockFacing particleFacing;

        private void initSoundsAndTicking()
        {
            fuelBlock = Api.World.BlockAccessor.GetBlock(FuelPos);

            l1 = Blockentity.RegisterGameTickListener(OnTick, 25);
            if (Api.Side == EnumAppSide.Server)
            {
                l2 = Blockentity.RegisterGameTickListener(OnSlowServerTick, 1000);
            }

            wsys = Api.ModLoader.GetModSystem<WeatherSystemBase>();

            if (ambientSound == null && Api.Side == EnumAppSide.Client)
            {
                ambientSound = ((IClientWorldAccessor)Api.World).LoadSound(new SoundParams()
                {
                    Location = new AssetLocation("sounds/environment/fire.ogg"),
                    ShouldLoop = true,
                    Position = FirePos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
                    DisposeOnFinish = false,
                    Volume = 1f
                });

                if (ambientSound != null)
                {
                    ambientSound.PlaybackPosition = ambientSound.SoundLengthSeconds * (float)Api.World.Rand.NextDouble();
                    ambientSound.Start();
                }
            }

            particleFacing = BlockFacing.FromNormal(new Vec3i(FirePos.X - FuelPos.X, FirePos.Y - FuelPos.Y, FirePos.Z - FuelPos.Z));
        }





        private void OnSlowServerTick(float dt)
        {
            if (!canBurn(FuelPos))
            {
                KillFire(false);
                return;
            }

            Entity[] entities = Api.World.GetEntitiesAround(FirePos.ToVec3d().Add(0.5, 0.5, 0.5), 3, 3, (e) => true);
            Vec3d ownPos = FirePos.ToVec3d();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!CollisionTester.AabbIntersect(entity.SelectionBox, entity.ServerPos.X, entity.ServerPos.Y, entity.ServerPos.Z, fireCuboid, ownPos)) continue;

                if (entity.Alive)
                {
                    entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Block, SourceBlock = fireBlock, SourcePos = ownPos, Type = EnumDamageType.Fire }, 2f);
                }

                if (Api.World.Rand.NextDouble() < 0.125)
                {
                    entity.Ignite();
                }
            }

            if (FuelPos != FirePos && Api.World.BlockAccessor.GetBlock(FirePos, BlockLayersAccess.Fluid).LiquidCode == "water")
            {
                KillFire(false);
                return;
            }

            if (Api.World.BlockAccessor.GetRainMapHeightAt(FirePos.X, FirePos.Z) <= FirePos.Y)   // It's more efficient to do this quick check before GetPrecipitation
            {
                // Die on rainfall
                tmpPos.Set(FirePos.X + 0.5, FirePos.Y + 0.5, FirePos.Z + 0.5);
                double rain = wsys.GetPrecipitation(tmpPos);
                if (rain > 0.15)
                {
                    Api.World.PlaySoundAt(new AssetLocation("sounds/effect/extinguish"), FirePos.X + 0.5, FirePos.Y, FirePos.Z + 0.5, null, false, 16);

                    if (rand.NextDouble() < rain / 2)
                    {
                        KillFire(false);
                        return;
                    }
                }
            }
        }


        private void OnTick(float dt)
        {
            if (Api.Side == EnumAppSide.Server)
            {
                remainingBurnDuration -= dt;

                OnFireTick?.Invoke(dt);

                float spreadChance = (TimePassed - 2.5f) / 450f;

                if (((ICoreServerAPI)Api).Server.Config.AllowFireSpread && spreadChance > Api.World.Rand.NextDouble())
                {
                    TrySpreadFireAllDirs();
                }
            }

            if (Api.Side == EnumAppSide.Client)
            {
                int index = Math.Min(fireBlock.ParticleProperties.Length - 1, Api.World.Rand.Next(fireBlock.ParticleProperties.Length + 1));
                AdvancedParticleProperties particles = fireBlock.ParticleProperties[index];
                
                particles.basePos = RandomBlockPos(Api.World.BlockAccessor, FuelPos, fuelBlock, particleFacing);

                particles.Quantity.avg = 0.75f;
                particles.TerrainCollision = false;
                Api.World.SpawnParticles(particles);
                particles.Quantity.avg = 0;
            }
        }




        public void KillFire(bool consumeFuel)
        {
            IsBurning = false;
            Blockentity.UnregisterGameTickListener(l1);
            Blockentity.UnregisterGameTickListener(l2);
            ambientSound?.FadeOutAndStop(1);
            OnFireDeath(consumeFuel);
        }


        protected void TrySpreadFireAllDirs()
        {
            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                BlockPos npos = FirePos.AddCopy(facing);
                TrySpreadTo(npos);
            }

            if (FuelPos != FirePos)
            {
                TrySpreadTo(FirePos);
            }
        }


        public bool TrySpreadTo(BlockPos pos)
        {
            // 1. Replaceable test
            var block = Api.World.BlockAccessor.GetBlock(pos);
            if (block.Replaceable < 6000) return false;

            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be?.GetBehavior<BEBehaviorBurning>() != null) return false;

            // 2. fuel test
            bool hasFuel = false;
            BlockPos npos = null;
            foreach (BlockFacing firefacing in BlockFacing.ALLFACES)
            {
                npos = pos.AddCopy(firefacing);
                block = Api.World.BlockAccessor.GetBlock(npos);
                if (canBurn(npos) && Api.World.BlockAccessor.GetBlockEntity(npos)?.GetBehavior<BEBehaviorBurning>() == null) {
                    hasFuel = true; 
                    break; 
                }
            }
            if (!hasFuel) return false;

            // 3. Land claim test
            IPlayer player = Api.World.PlayerByUid(startedByPlayerUid);            
            if (player != null && (Api.World.Claims.TestAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted || Api.World.Claims.TestAccess(player, npos, EnumBlockAccessFlags.BuildOrBreak) != EnumWorldAccessResponse.Granted)) {
                return false;
            }


            Api.World.BlockAccessor.SetBlock(fireBlock.BlockId, pos);

            //Api.World.Logger.Debug(string.Format("Fire @{0}: Spread to {1}.", FirePos, pos));

            BlockEntity befire = Api.World.BlockAccessor.GetBlockEntity(pos);
            befire.GetBehavior<BEBehaviorBurning>()?.OnFirePlaced(pos, npos, startedByPlayerUid);

            return true;
        }


        protected bool canBurn(BlockPos pos)
        {
            return 
                OnCanBurn(pos) 
                && Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) != true
            ;
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            killAmbientSound();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            killAmbientSound();
        }

        ~BEBehaviorBurning()
        {
            killAmbientSound();
        }

        void killAmbientSound()
        {
            if (ambientSound != null)
            {
                ambientSound?.Stop();
                ambientSound?.Dispose();
                ambientSound = null;
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            remainingBurnDuration = tree.GetFloat("remainingBurnDuration");
            startDuration = tree.GetFloat("startDuration");

            // pre v1.15-pre.3 fire
            if (!tree.HasAttribute("fireposX"))
            {
                BlockFacing fromFacing = BlockFacing.ALLFACES[tree.GetInt("fromFacing", 0)];
                FirePos = Blockentity.Pos.Copy();
                FuelPos = FirePos.AddCopy(fromFacing);
            }
            else
            {
                FirePos = new BlockPos(tree.GetInt("fireposX"), tree.GetInt("fireposY"), tree.GetInt("fireposZ"));
                FuelPos = new BlockPos(tree.GetInt("fuelposX"), tree.GetInt("fuelposY"), tree.GetInt("fuelposZ"));
            }

            bool wasBurning = IsBurning;
            bool nowBurning = tree.GetBool("isBurning", true);

            if (nowBurning && !wasBurning)
            {
                startBurning();
            }
            if (!nowBurning && wasBurning)
            {
                KillFire(remainingBurnDuration <= 0);
                IsBurning = nowBurning;
            }

            startedByPlayerUid = tree.GetString("startedByPlayerUid");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("remainingBurnDuration", remainingBurnDuration);
            tree.SetFloat("startDuration", startDuration);
            tree.SetBool("isBurning", IsBurning);

            tree.SetInt("fireposX", FirePos.X); tree.SetInt("fireposY", FirePos.Y); tree.SetInt("fireposZ", FirePos.Z);
            tree.SetInt("fuelposX", FuelPos.X); tree.SetInt("fuelposY", FuelPos.Y); tree.SetInt("fuelposZ", FuelPos.Z);

            if (startedByPlayerUid != null)
            {
                tree.SetString("startedByPlayerUid", startedByPlayerUid);
            }
        }


        static Random rand = new Random();
        public static Vec3d RandomBlockPos(IBlockAccessor blockAccess, BlockPos pos, Block block, BlockFacing facing = null)
        {
            if (facing == null)
            {
                Cuboidf[] selectionBoxes = block.GetSelectionBoxes(blockAccess, pos);
                Cuboidf box = (selectionBoxes != null && selectionBoxes.Length > 0) ? selectionBoxes[0] : Block.DefaultCollisionBox;

                return new Vec3d(
                    pos.X + box.X1 + rand.NextDouble() * box.XSize,
                    pos.Y + box.Y1 + rand.NextDouble() * box.YSize,
                    pos.Z + box.Z1 + rand.NextDouble() * box.ZSize
                );
            }
            else
            {
                Vec3i face = facing.Normali;

                Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccess, pos);

                bool haveCollisionBox = collisionBoxes != null && collisionBoxes.Length > 0;

                Vec3d basepos = new Vec3d(
                    pos.X + 0.5f + face.X / 1.95f + (haveCollisionBox && facing.Axis == EnumAxis.X ? (face.X > 0 ? collisionBoxes[0].X2 - 1 : collisionBoxes[0].X1) : 0),
                    pos.Y + 0.5f + face.Y / 1.95f + (haveCollisionBox && facing.Axis == EnumAxis.Y ? (face.Y > 0 ? collisionBoxes[0].Y2 - 1 : collisionBoxes[0].Y1) : 0),
                    pos.Z + 0.5f + face.Z / 1.95f + (haveCollisionBox && facing.Axis == EnumAxis.Z ? (face.Z > 0 ? collisionBoxes[0].Z2 - 1 : collisionBoxes[0].Z1) : 0)
                );

                Vec3d posVariance = new Vec3d(
                    1f * (1 - face.X),
                    1f * (1 - face.Y),
                    1f * (1 - face.Z)
                );

                return new Vec3d(
                    basepos.X + (rand.NextDouble() - 0.5) * posVariance.X,
                    basepos.Y + (rand.NextDouble() - 0.5) * posVariance.Y,
                    basepos.Z + (rand.NextDouble() - 0.5) * posVariance.Z
                );
            }
        }

    }
}
