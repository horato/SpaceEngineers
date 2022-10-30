using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
	partial class Program : MyGridProgram
	{
		private const string PRINTER_DRILL = "PRINTER_DRILL";
		private const string PRINTER_MERGE_TARGET = "PRINTER_MERGE_TARGET";
		private const string PRINTER_MERGE_SOURCE = "PRINTER_MERGE_SOURCE";
		private const string PRINTER_PISTON_SOURCE = "PRINTER_PISTON_SOURCE";
		private const string PRINTER_PROJECTOR = "PRINTER_PROJECTOR";
		private const string PRINTER_BASE_CONNECTOR = "PRINTER_BASE_CONNECTOR";
		private const string PRINTER_CONTROL_SEAT = "PRINTER_CONTROL_SEAT";
		private const string MAIN_CONTAINER = "MAIN_CONTAINER";

		private const int DRILL_INVENTORY_MAX_THRESHOLD = 90;
		private const int DRILL_INVENTORY_MIN_THRESHOLD = 90;
		private const int DEFAULT_CONTAINER_MAX = 50;

		private MiningPlatform _miningPlatform;
		private PlatformState _platformState = PlatformState.Unknown;
		private DateTime _pistonExtensionStartTime;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		public void Main(string argument, UpdateType updateSource)
		{
			if (_miningPlatform == null || updateSource.HasFlag(UpdateType.Once))
				RefreshConnectedBlocks();

			DisplayInfo();
			if (_miningPlatform.IsMainContainerStorageFull())
			{
				_miningPlatform.DisableWelders();
				_miningPlatform.DisableDrills();
				_miningPlatform.DisableGrinders();
				return;
			}

			_miningPlatform.EnableWelders();
			_miningPlatform.EnableDrills();
			_miningPlatform.EnableGrinders();
			switch (_platformState)
			{
				case PlatformState.Unknown:
					RevertToDefaultState();
					break;
				case PlatformState.Default:
					OnDefault();
					break;
				case PlatformState.SourcePistonExtending:
					OnSourcePistonExtending();
					break;
				case PlatformState.FirstMergeBlockReached:
					OnFirstMergeBlockReached();
					break;
				case PlatformState.SourcePistonExtended:
					OnSourcePistonExtended();
					break;
				case PlatformState.SourcePistonRetracting:
					OnSourcePistonRetracting();
					break;
				case PlatformState.SourceMergeBlockLocked:
					OnSourceMergeBlockLocked();
					break;
				case PlatformState.SourcePistonRetracted:
					OnSourcePistonRetracted();
					break;
				case PlatformState.DrillsStorageFull:
					OnDrillsStorageFull();
					break;
				default:
					// Why tf is ArgumentOutOfRange forbidden? lmao
					throw new InvalidOperationException($"Unknown value {_platformState}");
			}

			DisplayInfo();
		}

		private int GetMax()
		{
			if (string.IsNullOrWhiteSpace(Me.CustomData))
				return DEFAULT_CONTAINER_MAX;

			// Who tf uses 7 year old lang version?
			int max;
			if (!int.TryParse(Me.CustomData, out max))
				return DEFAULT_CONTAINER_MAX;

			return max;
		}

		private void RevertToDefaultState()
		{
			_miningPlatform.EnableTargetMergeBlock();
			_miningPlatform.RetractSourcePiston();

			if (_miningPlatform.IsTargetMergeBlockLocked())
				_miningPlatform.DisableSourceMergeBlock();

			if (_miningPlatform.IsSourcePistonRetracted())
			{
				_miningPlatform.EnableSourceMergeBlock();
				_platformState = PlatformState.Default;
			}
		}

		private void OnDefault()
		{
			// Wait for welders
			if (!_miningPlatform.IsProjectionBuilt())
				return;

			_miningPlatform.ExtendSourcePiston();
			_platformState = PlatformState.SourcePistonExtending;
			_pistonExtensionStartTime = DateTime.Now;
		}

		private void OnSourcePistonExtending()
		{
			// Can't find a better way to determine whether the piston already reached the first merge block
			if (DateTime.Now - _pistonExtensionStartTime < TimeSpan.FromSeconds(5))
				return;

			_pistonExtensionStartTime = DateTime.MaxValue;
			_platformState = PlatformState.FirstMergeBlockReached;
		}

		private void OnFirstMergeBlockReached()
		{
			if (_miningPlatform.IsDrillStorageFull())
			{
				_platformState = PlatformState.DrillsStorageFull;
				return;
			}

			if (_miningPlatform.IsTargetMergeBlockLocked())
				_miningPlatform.DisableTargetMergeBlock();
			if (_miningPlatform.IsBaseConnectorEngaged())
				_miningPlatform.DisengageBaseConnector();
			if (_miningPlatform.IsSourcePistonExtended())
				_platformState = PlatformState.SourcePistonExtended;
		}

		private void OnSourcePistonExtended()
		{
			_miningPlatform.EngageBaseConnector();
			_miningPlatform.EnableTargetMergeBlock();
			if (_miningPlatform.IsTargetMergeBlockLocked())
				_miningPlatform.DisableSourceMergeBlock();

			if (_miningPlatform.IsBaseConnectorEngaged() && _miningPlatform.IsTargetMergeBlockLocked() && !_miningPlatform.IsSourceMergeBlockLocked())
			{
				_miningPlatform.RetractSourcePiston();
				_platformState = PlatformState.SourcePistonRetracting;
			}
		}

		private void OnSourcePistonRetracting()
		{
			if (!_miningPlatform.IsTargetMergeBlockLocked() || _miningPlatform.IsSourceMergeBlockLockable())
				return;

			_miningPlatform.EnableSourceMergeBlock();
			_platformState = PlatformState.SourceMergeBlockLocked;
		}

		private void OnSourceMergeBlockLocked()
		{
			if (_miningPlatform.IsSourcePistonRetracted())
				_platformState = PlatformState.SourcePistonRetracted;
		}

		private void OnSourcePistonRetracted()
		{
			if (_miningPlatform.IsProjectionBuilt())
				_platformState = PlatformState.Default;
		}

		private void OnDrillsStorageFull()
		{
			if (_miningPlatform.CanEngageBaseConnector())
			{
				_miningPlatform.EngageBaseConnector();
			}
			else if (_miningPlatform.IsDrillStorageEmpty())
			{
				_miningPlatform.ExtendSourcePiston();
				_platformState = PlatformState.FirstMergeBlockReached;
			}
			else
			{
				_miningPlatform.RetractSourcePiston();
			}
		}

		private void DisplayInfo()
		{
			_miningPlatform.WriteOutputText(0, () =>
			{
				var sb = new StringBuilder();
				sb.AppendLine($"State {_platformState}");
				sb.AppendLine($"Target locked: {_miningPlatform.IsTargetMergeBlockLocked()}");
				sb.AppendLine($"Source locked: {_miningPlatform.IsSourceMergeBlockLocked()}");
				sb.AppendLine($"Source lockable: {_miningPlatform.IsSourceMergeBlockLockable()}");
				sb.AppendLine($"Piston retracted: {_miningPlatform.IsSourcePistonRetracted()}");
				sb.AppendLine($"Piston extended: {_miningPlatform.IsSourcePistonExtended()}");
				sb.AppendLine($"Projection built: {_miningPlatform.IsProjectionBuilt()}");
				sb.AppendLine($"Connector engaged: {_miningPlatform.IsBaseConnectorEngaged()}");
				sb.AppendLine($"Drill capacity: {_miningPlatform.GetAverageDrillStoragePercentage()}%");
				sb.AppendLine($"Main container capacity: {_miningPlatform.GetMainContainerStoragePercentage()}%");
				sb.AppendLine($"Main container max: {_miningPlatform.MaxMainContainerPercentage}%");

				return sb;
			});

			_miningPlatform.WriteOutputText(1, () =>
			{
				var sb = new StringBuilder();
				sb.AppendLine($"Enabled: {!_miningPlatform.IsMainContainerStorageFull()}");

				return sb;
			});
		}

		private void RefreshConnectedBlocks()
		{
			// Drills
			var drills = new List<IMyShipDrill>();
			GridTerminalSystem.GetBlocksOfType(drills, x => x.CustomData == PRINTER_DRILL);

			var drillGrids = drills.Select(x => x.CubeGrid).Distinct().ToList();
			if (drillGrids.Count != 1)
				throw Terminate($"Found drills marked as {PRINTER_DRILL} on more than one grid. Cannot continue.");

			var drillGrid = drillGrids.Single();
			GridTerminalSystem.GetBlocksOfType(drills, x => x.CubeGrid == drillGrid);

			// Connectors
			var connectors = new List<IMyShipConnector>();
			GridTerminalSystem.GetBlocksOfType(connectors, x => x.CubeGrid == drillGrid);

			// Base Connector
			var baseConnectors = new List<IMyShipConnector>();
			GridTerminalSystem.GetBlocksOfType(baseConnectors, x => x.CustomData == PRINTER_BASE_CONNECTOR);
			if (baseConnectors.Count != 1)
				throw Terminate($"Found {baseConnectors.Count} connectors marked as {PRINTER_BASE_CONNECTOR} instead of expected 1.");

			// Target merge block
			var targetMergeBlocks = new List<IMyShipMergeBlock>();
			GridTerminalSystem.GetBlocksOfType(targetMergeBlocks, x => x.CustomData == PRINTER_MERGE_TARGET);
			if (targetMergeBlocks.Count != 1)
				throw Terminate($"Found {targetMergeBlocks.Count} merge blocks marked as {PRINTER_MERGE_TARGET} instead of expected 1.");

			// Source merge block
			var sourceMergeBlocks = new List<IMyShipMergeBlock>();
			GridTerminalSystem.GetBlocksOfType(sourceMergeBlocks, x => x.CustomData == PRINTER_MERGE_SOURCE);
			if (sourceMergeBlocks.Count != 1)
				throw Terminate($"Found {sourceMergeBlocks.Count} merge blocks marked as {PRINTER_MERGE_SOURCE} instead of expected 1.");

			// Source piston
			var sourcePistons = new List<IMyPistonBase>();
			GridTerminalSystem.GetBlocksOfType(sourcePistons, x => x.CustomData == PRINTER_PISTON_SOURCE);
			if (sourcePistons.Count != 1)
				throw Terminate($"Found {sourcePistons.Count} pistons marked as {PRINTER_PISTON_SOURCE} instead of expected 1.");

			// Welders
			var sourcePiston = sourcePistons.Single();
			var welders = new List<IMyShipWelder>();
			GridTerminalSystem.GetBlocksOfType(welders, x => x.CubeGrid == sourcePiston.CubeGrid);
			if (welders.Count == 0)
				throw Terminate($"Found {welders.Count} welders instead of expected >0.");

			// Projector
			var projectors = new List<IMyProjector>();
			GridTerminalSystem.GetBlocksOfType(projectors, x => x.CustomData == PRINTER_PROJECTOR);
			if (projectors.Count != 1)
				throw Terminate($"Found {projectors.Count} projectors marked as {PRINTER_PROJECTOR} instead of expected 1.");

			// Control seat
			var controlSeats = new List<IMyCockpit>();
			GridTerminalSystem.GetBlocksOfType(controlSeats, x => x.CustomData == PRINTER_CONTROL_SEAT);
			if (projectors.Count > 1)
				throw Terminate($"Found {projectors.Count} blocks marked as {PRINTER_PROJECTOR} instead of expected 0-1.");

			// Main cargo
			var cargoes = new List<IMyCargoContainer>();
			GridTerminalSystem.GetBlocksOfType(cargoes, x => x.CustomData == MAIN_CONTAINER);
			if (projectors.Count != 1)
				throw Terminate($"Found {cargoes.Count} cargo containers marked as {MAIN_CONTAINER} instead of expected 1.");

			// Grinders
			var grinders = new List<IMyShipGrinder>();
			GridTerminalSystem.GetBlocksOfType(grinders, x => x.CubeGrid == sourcePiston.CubeGrid);

			_miningPlatform = new MiningPlatform
			(
				drills,
				connectors,
				targetMergeBlocks.Single(),
				sourceMergeBlocks.Single(),
				sourcePiston,
				projectors.Single(),
				baseConnectors.Single(),
				controlSeats.SingleOrDefault(),
				welders,
				cargoes.Single(),
				grinders,
				GetMax()
			);
		}

		private Exception Terminate(string msg)
		{
			Echo(msg);
			return new InvalidOperationException(msg);
		}

		public class MiningPlatform
		{
			public IEnumerable<IMyShipDrill> Drills { get; }
			public IEnumerable<IMyShipConnector> DrillConnectors { get; }
			public IMyShipMergeBlock TargetBlock { get; }
			public IMyShipMergeBlock SourceBlock { get; }
			public IMyPistonBase SourcePiston { get; }
			public IMyProjector Projector { get; }
			public IMyShipConnector BaseConnector { get; }
			public IMyCockpit ControlSeat { get; }
			public IEnumerable<IMyShipWelder> Welders { get; }
			public IMyCargoContainer MainContainer { get; }
			public IEnumerable<IMyShipGrinder> Grinders { get; }
			public int MaxMainContainerPercentage { get; }

			public MiningPlatform(IEnumerable<IMyShipDrill> drills, IEnumerable<IMyShipConnector> drillConnectors, IMyShipMergeBlock targetBlock, IMyShipMergeBlock sourceBlock, IMyPistonBase sourcePiston, IMyProjector projector, IMyShipConnector baseConnector, IMyCockpit controlSeat, IEnumerable<IMyShipWelder> welders, IMyCargoContainer mainContainer, IEnumerable<IMyShipGrinder> grinders, int maxMainContainerPercentage)
			{
				Drills = drills;
				DrillConnectors = drillConnectors;
				TargetBlock = targetBlock;
				SourceBlock = sourceBlock;
				SourcePiston = sourcePiston;
				Projector = projector;
				BaseConnector = baseConnector;
				ControlSeat = controlSeat;
				Welders = welders;
				MainContainer = mainContainer;
				Grinders = grinders;
				MaxMainContainerPercentage = maxMainContainerPercentage;
			}

			public void EnableTargetMergeBlock() => EnableMergeBlock(TargetBlock);
			public void DisableTargetMergeBlock() => DisableMergeBlock(TargetBlock);
			public bool IsTargetMergeBlockLocked() => IsMergeBlockLocked(TargetBlock);
			public void DisableSourceMergeBlock() => DisableMergeBlock(SourceBlock);
			public void EnableSourceMergeBlock() => EnableMergeBlock(SourceBlock);
			public bool IsSourceMergeBlockLocked() => IsMergeBlockLocked(SourceBlock);

			public bool IsSourceMergeBlockLockable()
			{
				return SourceBlock.State == MergeState.Constrained;
			}

			private void EnableMergeBlock(IMyShipMergeBlock block)
			{
				if (!block.Enabled)
					block.Enabled = true;
			}

			private void DisableMergeBlock(IMyShipMergeBlock block)
			{
				if (block.Enabled)
					block.Enabled = false;
			}

			private bool IsMergeBlockLocked(IMyShipMergeBlock block)
			{
				return block.Enabled && block.State == MergeState.Locked;
			}

			public void RetractSourcePiston()
			{
				SourcePiston.MinLimit = SourcePiston.LowestPosition;
				SourcePiston.Velocity = -1f;
			}

			public void ExtendSourcePiston()
			{
				SourcePiston.MaxLimit = SourcePiston.HighestPosition;
				SourcePiston.Velocity = 1f;
			}

			public bool IsSourcePistonRetracted()
			{
				return SourcePiston.CurrentPosition <= SourcePiston.LowestPosition;
			}

			public bool IsSourcePistonExtended()
			{
				return SourcePiston.CurrentPosition >= SourcePiston.HighestPosition;
			}

			public bool IsProjectionBuilt()
			{
				return Projector.IsProjecting && Projector.Enabled && Projector.RemainingBlocks == 0 && Projector.RemainingArmorBlocks == 0;
			}

			public void EnableWelders() => ChangeWeldersState(true);
			public void EnableDrills() => ChangeDrillsState(true);
			public void DisableWelders() => ChangeWeldersState(false);
			public void DisableDrills() => ChangeDrillsState(false);
			public void EnableGrinders() => ChangeGrindersState(true);
			public void DisableGrinders() => ChangeGrindersState(false);

			private void ChangeWeldersState(bool enabled)
			{
				foreach (var welder in Welders)
				{
					welder.UseConveyorSystem = true;
					welder.HelpOthers = false;
					welder.Enabled = enabled;
				}
			}

			private void ChangeDrillsState(bool enabled)
			{
				foreach (var drill in Drills)
				{
					drill.UseConveyorSystem = true;
					drill.Enabled = enabled;
				}
			}

			private void ChangeGrindersState(bool enabled)
			{
				foreach (var grinder in Grinders)
				{
					grinder.UseConveyorSystem = true;
					grinder.Enabled = enabled;
				}
			}

			public bool IsBaseConnectorEngaged()
			{
				return BaseConnector.Status == MyShipConnectorStatus.Connected;
			}

			public void EngageBaseConnector()
			{
				BaseConnector.Connect();
			}

			public void DisengageBaseConnector()
			{
				BaseConnector.Disconnect();
			}

			public bool CanEngageBaseConnector()
			{
				return BaseConnector.Status == MyShipConnectorStatus.Connectable;
			}

			public double GetAverageDrillStoragePercentage()
			{
				var inventories = Drills.Select(x => x.GetInventory(0)).ToList();
				return Math.Round(inventories.Sum(x => GetInventoryPercentage(x)) / inventories.Count);
			}

			public bool IsDrillStorageFull()
			{
				return GetAverageDrillStoragePercentage() >= DRILL_INVENTORY_MAX_THRESHOLD;
			}

			public bool IsDrillStorageEmpty()
			{
				return GetAverageDrillStoragePercentage() < DRILL_INVENTORY_MIN_THRESHOLD;
			}

			private double GetInventoryPercentage(IMyInventory inventory)
			{
				var current = (double)inventory.CurrentVolume.RawValue;
				var max = (double)inventory.MaxVolume.RawValue;
				return Math.Round((current / max) * 100f, 2);
			}

			public double GetMainContainerStoragePercentage()
			{
				return GetInventoryPercentage(MainContainer.GetInventory(0));
			}

			public bool IsMainContainerStorageFull()
			{
				return GetMainContainerStoragePercentage() >= MaxMainContainerPercentage;
			}

			public void WriteOutputText(int index, Func<StringBuilder> sb)
			{
				var surface = ControlSeat?.GetSurface(index);
				if (surface == null)
					return;

				surface.ContentType = ContentType.TEXT_AND_IMAGE;
				surface.Alignment = TextAlignment.CENTER;

				surface.WriteText(sb());
			}
		}

		public enum PlatformState
		{
			Unknown = 0,

			/// <summary> Default state - Source piston retracted, source merge block enabled, welders finished, target merge block locked, base connector engaged </summary>
			Default = 1,

			/// <summary> Source piston is moving towards the first merge block </summary>
			SourcePistonExtending = 2,

			/// <summary> First merge block reached. Disable target merge block, disengage base connector </summary>
			FirstMergeBlockReached = 3,

			/// <summary> Source piston is at maximum range. Enable target merge block, disable source merge block, engage base connector </summary>
			SourcePistonExtended = 4,

			/// <summary> Source piston retracting to base position </summary>
			SourcePistonRetracting = 5,

			/// <summary> Source merge block enabled - welders begin to operate </summary>
			SourceMergeBlockLocked = 6,

			/// <summary> Source piston retracted, waiting for welders to finish </summary>
			SourcePistonRetracted = 7,

			// End of ideal scenario. Reverting back to default

			/// <summary> 3a - Drills storage capacity have reached a threshold - Retract, engage base connector, let drills empty, continue drilling </summary>
			DrillsStorageFull = 8
		}
	}
}
