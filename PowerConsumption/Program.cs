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
		private readonly MyDefinitionId _electricityDefinition = MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Electricity");
		private readonly MyDefinitionId _hydrogenDefinition = MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Hydrogen");
		private readonly MyDefinitionId _oxygenDefinition = MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Oxygen");
		private ResourceType _resourceType = ResourceType.Electricity;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		public void Main(string argument, UpdateType updateSource)
		{
			if (argument == "ELECTRICITY")
				_resourceType = ResourceType.Electricity;
			else if (argument == "HYDROGEN")
				_resourceType = ResourceType.Hydrogen;
			else if (argument == "OXYGEN")
				_resourceType = ResourceType.Oxygen;

			switch (_resourceType)
			{
				case ResourceType.Electricity:
					ExecutePowerConsumption(_electricityDefinition, "MW");
					break;
				case ResourceType.Hydrogen:
					ExecutePowerConsumption(_hydrogenDefinition, "l/s");
					break;
				case ResourceType.Oxygen:
					ExecutePowerConsumption(_oxygenDefinition, "l/s");
					break;
				default:
					throw new InvalidOperationException($"Unknown resource type {_resourceType}");
			}
		}

		private void ExecutePowerConsumption(MyDefinitionId resourceType, string unit)
		{
			var blockValues = new Dictionary<long, BlockInfo>();
			var blocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType(blocks);
			foreach (var block in blocks)
			{
				AppendProducers(resourceType, block, blockValues);
				AppendConsumers(resourceType, block, blockValues);
			}

			var sb = new StringBuilder();
			sb.AppendLine($"{_resourceType} Current:");
			sb.AppendLine($"+{Math.Round(blockValues.Sum(x => x.Value.ProduceCurrent), 2)} {unit}");
			sb.AppendLine($"-{Math.Round(blockValues.Sum(x => x.Value.ConsumeCurrent), 2)} {unit}");

			sb.AppendLine();

			sb.AppendLine($"{_resourceType} Max:");
			sb.AppendLine($"+{Math.Round(blockValues.Sum(x => x.Value.ProduceMax), 2)} {unit}");
			sb.AppendLine($"-{Math.Round(blockValues.Sum(x => x.Value.ConsumeMax), 2)} {unit}");

			var blockInputs = blockValues.OrderByDescending(x => x.Value.ConsumeCurrent).ToList();
			sb.AppendLine();
			for (var i = 0; i < 8; i++)
			{
				if (blockInputs.Count - 1 < i)
					continue;

				var block = blockInputs[i];
				if (block.Value.ConsumeCurrent == 0)
					continue;

				sb.AppendLine($"{block.Value.BlockName}: {Math.Round(block.Value.ConsumeCurrent, 2)} {unit}");
			}

			var lcd = new List<IMyTextPanel>();
			GridTerminalSystem.GetBlocksOfType(lcd, x => x.CustomData == "LCD");
			lcd.ForEach(x => x.WriteText(sb));
		}

		private void AppendConsumers(MyDefinitionId resourceType, IMyTerminalBlock block, IDictionary<long, BlockInfo> blockValues)
		{
			var sink = block.Components.Get<MyResourceSinkComponent>();
			if (sink == null)
				return;
			if (sink.AcceptedResources.All(x => x != resourceType))
				return;
			if (!blockValues.ContainsKey(block.EntityId))
				blockValues.Add(block.EntityId, new BlockInfo(block.CustomName));

			var onlyCurrent = block is IMyGasTank || block is IMyBatteryBlock || block is IMyThrust;
			var blockValue = blockValues[block.EntityId];
			var current = sink.CurrentInputByType(resourceType);
			var max = onlyCurrent ? 0 : sink.MaxRequiredInputByType(resourceType);
			blockValue.AddConsume(current, max);
		}

		private void AppendProducers(MyDefinitionId resourceType, IMyTerminalBlock block, IDictionary<long, BlockInfo> blockValues)
		{
			var source = block.Components.Get<MyResourceSourceComponent>();
			if (source == null)
				return;
			if (source.ResourceTypes.All(x => x != resourceType))
				return;
			if (!blockValues.ContainsKey(block.EntityId))
				blockValues.Add(block.EntityId, new BlockInfo(block.CustomName));

			var onlyCurrent = block is IMyGasTank || block is IMyBatteryBlock || block is IMyThrust;
			var blockValue = blockValues[block.EntityId];
			var current = source.CurrentOutputByType(resourceType);
			var max = onlyCurrent ? 0 : source.MaxOutputByType(resourceType);
			blockValue.AddProduce(current, max);
		}

		private class BlockInfo
		{
			public string BlockName { get; }
			public float ProduceCurrent { get; private set; }
			public float ProduceMax { get; private set; }
			public float ConsumeCurrent { get; private set; }
			public float ConsumeMax { get; private set; }

			public BlockInfo(string blockName)
			{
				BlockName = blockName;
			}

			public void AddProduce(float current, float max)
			{
				ProduceCurrent += current;
				ProduceMax += max;
			}

			public void AddConsume(float current, float max)
			{
				ConsumeCurrent += current;
				ConsumeMax += max;
			}
		}

		private enum ResourceType
		{
			Electricity = 1,
			Hydrogen = 2,
			Oxygen = 3
		}
	}
}
