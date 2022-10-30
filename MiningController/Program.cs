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

namespace MiningController
{
	partial class Program : MyGridProgram
	{
		private const int DEFAULT_MIN = 10;
		private const int DEFAULT_MAX = 50;
		private const string CONTAINER = "MAIN_CONTAINER";
		private const string DRILL = "MAIN_DRILL";
		private const string ROTOR = "MAIN_ROTOR";
		private const string PISTON = "MAIN_PISTON";
		private bool _isGathering = false;
		private bool _canIncreasePistonMaxDistance;
		private bool _rotorsMadeFullCircle;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		public void Main(string argument, UpdateType updateSource)
		{
			var cargoes = new List<IMyCargoContainer>();
			GridTerminalSystem.GetBlocksOfType(cargoes, x => x.CustomData == CONTAINER);

			var max = GetMax();
			Echo($"Max percentage set: {max}");
			var isGatheringChange = UpdateIsGathering(cargoes, DEFAULT_MIN, max);

			UpdateLcdText(cargoes);
			ToggleDrills(_isGathering);
			ToggleRotors(_isGathering);
			ToggleAlertLight(_isGathering);
			ExtendPistons(_isGathering);

			if (isGatheringChange)
				PlayAlertSound();
		}

		private int GetMax()
		{
			if (string.IsNullOrWhiteSpace(Me.CustomData))
				return DEFAULT_MAX;

			// Who tf uses 7 year old lang version?
			int max;
			if (!int.TryParse(Me.CustomData, out max))
				return DEFAULT_MAX;

			return max;
		}

		private bool UpdateIsGathering(List<IMyCargoContainer> cargoes, int min, int max)
		{
			var anyMin = cargoes.Any(x => GetInventoryPercentage(x.GetInventory()) < min);
			var anyMax = cargoes.Any(x => GetInventoryPercentage(x.GetInventory()) > max);

			if (_isGathering && anyMax)
			{
				_isGathering = false;
				return true;
			}

			if (!_isGathering && anyMin)
			{
				_isGathering = true;
				return true;
			}

			return false;
		}

		private void UpdateLcdText(List<IMyCargoContainer> cargoes)
		{
			var surface = Me.GetSurface(0);

			surface.ContentType = ContentType.TEXT_AND_IMAGE;
			surface.Alignment = TextAlignment.CENTER;
			surface.TextPadding = 40f;

			surface.WriteText("", false);
			cargoes.ForEach(x => surface.WriteText($"{x.CustomName} is at {GetInventoryPercentage(x.GetInventory())}% capacity", true));
			cargoes.ForEach(x => Echo($"{x.CustomName} is at {GetInventoryPercentage(x.GetInventory())}% capacity"));
		}

		private double GetInventoryPercentage(IMyInventory inventory)
		{
			var current = (double)inventory.CurrentVolume.RawValue;
			var max = (double)inventory.MaxVolume.RawValue;
			return Math.Round((current / max) * 100f, 2);
		}

		private void ToggleDrills(bool enabled)
		{
			var drills = new List<IMyShipDrill>();
			GridTerminalSystem.GetBlocksOfType(drills, x => x.CustomData == DRILL);
			drills.ForEach(x => x.Enabled = enabled);
		}

		private void ToggleRotors(bool enabled)
		{
			var rotors = new List<IMyMotorAdvancedStator>();
			GridTerminalSystem.GetBlocksOfType(rotors, x => x.CustomData == ROTOR);
			rotors.ForEach(x => x.TargetVelocityRPM = enabled ? 0.25f : 0f);

			// Angle isn't always precise, +- 5° is needed
			if (rotors.Any(x => IsInRange(GetAngleInDegrees(x.Angle), 175, 185)))
				_canIncreasePistonMaxDistance = true;
			if (rotors.Any(x => IsInRange(GetAngleInDegrees(x.Angle), 355, 5)))
				_rotorsMadeFullCircle = true;
		}

		private int GetAngleInDegrees(float angleInRadians)
		{
			var degrees = (180 / Math.PI) * angleInRadians;
			return (int)Math.Round(degrees);
		}

		private bool IsInRange(int angle, int start, int end)
		{
			start = Normalize(start, 0, 360);
			end = Normalize(end, 0, 360);
			angle = Normalize(angle, 0, 360);

			if (start <= end)
				return start <= angle && angle <= end;

			else
				return !(end < angle && angle < start);
		}

		private int Normalize(int angle, int left, int right)
		{
			var period = right - left;
			var temp = angle % period;
			if (temp < left)
				return temp + period;
			if (temp >= right)
				return temp - period;

			return temp;
		}

		private void ToggleAlertLight(bool enabled)
		{
			// Svetlo
			var lights = new List<IMyReflectorLight>();
			GridTerminalSystem.GetBlocksOfType(lights, x => x.CustomData == "ALERT_LIGHT");
			lights.ForEach(x => x.Enabled = enabled);

			// Rotor
			var lightStator = new List<IMyMotorStator>();
			GridTerminalSystem.GetBlocksOfType(lightStator, x => x.CustomData == "ALERT_LIGHT_ROTOR");
			lightStator.ForEach(x => x.TargetVelocityRPM = enabled ? 15f : 0f);
		}

		private void ExtendPistons(bool enabled)
		{
			if (!enabled || !_rotorsMadeFullCircle || !_canIncreasePistonMaxDistance)
				return;

			_rotorsMadeFullCircle = false;
			_canIncreasePistonMaxDistance = false;

			var pistons = new List<IMyPistonBase>();
			GridTerminalSystem.GetBlocksOfType(pistons, x => x.CustomData == PISTON);
			pistons.ForEach(x => x.MaxLimit = (float)Math.Round(x.CurrentPosition, 1));
			pistons.ForEach(x => x.Velocity = 0.2f);

			var piston = pistons.Where(x => x.CurrentPosition < x.HighestPosition).OrderByDescending(x => x.CurrentPosition).FirstOrDefault();
			if (piston == null)
				return;

			piston.MaxLimit += 0.5f;
		}

		private void PlayAlertSound()
		{
			var soundBlocks = new List<IMySoundBlock>();
			GridTerminalSystem.GetBlocksOfType(soundBlocks, x => x.CustomData == "ALERT_SOUND");
			soundBlocks.ForEach(x => x.Play());
		}
	}
}
