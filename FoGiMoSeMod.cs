using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;

namespace ForestGiantMotionsense
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class FoGiMoSeMod : BaseUnityPlugin
    {
	    private static ConfigEntry<float> _configMoveTime;
	    
	    private static FoGiMoSeMod _instance;
	    private static bool _patchFailed;
        private void Awake()
        {
	        // Store a debug instance
	        _instance = this;
	        
	        // Flag the patch as safe
	        _patchFailed = false;
	        
            // Log plugin load starting
            Logger.LogInfo($"Loading...");
            
            // Read the config
            _configMoveTime = Config.Bind("General",
                                         "TimeSinceMovingThreshold",
                                         2.25f,
                                         "The amount of time the player must remain still before being invisible to a Forest Giant.");
            
            // Apply the patches
            Harmony harmony = Harmony.CreateAndPatchAll(typeof(FoGiMoSeMod));
            
            // Check if the patch failed
            if (_patchFailed)
            {
	            // Log the failure
	            Logger.LogError($"Failure to find patch location, reverting!");
	            // Unpatch
	            Harmony.UnpatchID(harmony.Id);
	            return;
            }
            
            // Log plugin load success
            Logger.LogInfo($"Loaded!");
        }
        
        [HarmonyPatch(typeof(ForestGiantAI), "LookForPlayers")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> MotionPatch(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // Create the local variables to hold the if states
            LocalBuilder andConditionOne = generator.DeclareLocal(typeof(bool));
            LocalBuilder orConditionOne = generator.DeclareLocal(typeof(bool));
            
            // Transpile!
            CodeMatcher matcher = new CodeMatcher(instructions);
            matcher.MatchForward(true, // Find the branch destination label
                                 new CodeMatch(OpCodes.Call),
                                 new CodeMatch(OpCodes.Ldfld),
                                 new CodeMatch(OpCodes.Ldloc_S),
                                 new CodeMatch(OpCodes.Ldelem_Ref),
                                 new CodeMatch(OpCodes.Stloc_S))
                   .Advance(1);
            
            // Assertion for patch success
            if (matcher.Remaining == 0)
            {
	            _instance.Logger.LogFatal("Could not find first LookForPlayers patch, flagging abort!");
	            _patchFailed = true;
            }
            
            // Store the branch target instruction
            Label labelBranchTarget = matcher.Instruction.labels.First(); // Create the branch target
            
            matcher.MatchBack(false,
                            new CodeMatch(OpCodes.Ldarg_0),
                            new CodeMatch(OpCodes.Ldfld),
                            new CodeMatch(OpCodes.Ldloc_S),
                            new CodeMatch(OpCodes.Ldelem_R4),
                            new CodeMatch(OpCodes.Ldloc_S),
                            new CodeMatch(OpCodes.Ble_Un));
            
            // Assertion for patch success
            if (matcher.Remaining == 0)
            {
	            _instance.Logger.LogFatal("Could not find second LookForPlayers patch, flagging abort!");
	            _patchFailed = true;
            }
            
            // Cache the load ops
            CodeInstruction loadIndexTwo = matcher.Advance(2).Instruction;
            CodeInstruction loadNum2 = matcher.Advance(2).Instruction;
            matcher.Advance(-4); // Correct the position back to the start of the removed block
            
            // Clear the old opcodes out
            matcher.RemoveInstructions(6)                     // Cut out the old instructions
                   .InsertAndAdvance(                         // __ First AND Condition
                        new CodeInstruction(OpCodes.Ldarg_0), // Load 'this'
                        new CodeInstruction(OpCodes.Ldfld,    // Load the array
                                            AccessTools.Field(typeof(ForestGiantAI), nameof(ForestGiantAI.playerStealthMeters))),
                        loadIndexTwo,                           // Load the current player index
                        new CodeInstruction(OpCodes.Ldelem_R4), // Retrieve the current player's stealth value
                        loadNum2,                               // Load the worst stealth value
                        new CodeInstruction(OpCodes.Cgt),       // Compare the current stealth vs the worst
                        new CodeInstruction(OpCodes.Stloc_S,    // Store the first AND value
                                            andConditionOne),
                        // __ Second AND Condition
                        // First OR Condition
                        new CodeInstruction(OpCodes.Call, // Get the singleton reference of the "game manager"
                                            AccessTools.Property(typeof(StartOfRound), nameof(StartOfRound.Instance)).GetMethod),
                        new CodeInstruction(OpCodes.Ldfld, // Load the player array
												AccessTools.Field(typeof(StartOfRound), nameof(StartOfRound.allPlayerScripts))),
                        loadIndexTwo,                            // Load the index of the current player
                        new CodeInstruction(OpCodes.Ldelem_Ref), // Get the reference to the current player from the array
                        new CodeInstruction(OpCodes.Dup),        // Duplicate the reference
                        new CodeInstruction(OpCodes.Ldfld,       // Load the time since moving field
                                            AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.timeSincePlayerMoving))),
                        new CodeInstruction(OpCodes.Ldc_R4, _configMoveTime.Value), // Load the movement duration sensitivity
                        new CodeInstruction(OpCodes.Clt),                     // Check if the time since the  player's movement is less than the sensitivity threshold
                        new CodeInstruction(OpCodes.Stloc_S, orConditionOne), // Store the first OR condition
                        // Second OR Condition
                        // We already have the player reference on the stack
                        new CodeInstruction(OpCodes.Ldarg_0), // Get a pointer for 'this' forest giant
                        new CodeInstruction(OpCodes.Ldfld,    // Get the chasingPlayer on 'this' forest giant
                                            AccessTools.Field(typeof(ForestGiantAI), nameof(ForestGiantAI.chasingPlayer))),
                        new CodeInstruction(OpCodes.Call, 
                                            AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.Equals))), // Compare the two players
                        // We'd normally store the if from here, but we only have it on the stack and the next step is just comparisons so keep it on
                        // __ IF Statement
                        new CodeInstruction(OpCodes.Ldloc_S, orConditionOne),       // Load the other OR condition
                        new CodeInstruction(OpCodes.Or),                            // OR the two conditions
                        new CodeInstruction(OpCodes.Ldloc_S, andConditionOne),      // Load the AND condition
                        new CodeInstruction(OpCodes.And),                           // AND the two conditions
                        new CodeInstruction(OpCodes.Brfalse_S, labelBranchTarget)); // Branch to the end label if we're false
         
            
            // Return the finished instructions
            return matcher.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(PlayerControllerB), "UpdatePlayerPositionClientRpc")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> TimeSinceMovePatch(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new CodeMatcher(instructions)
                  .MatchForward(true,
                                new CodeMatch(OpCodes.Ldarg_0), 
                                new CodeMatch(OpCodes.Ldfld),
                                new CodeMatch(OpCodes.Ldfld),
                                new CodeMatch(OpCodes.Dup),
                                new CodeMatch(OpCodes.Ldfld),
                                new CodeMatch(OpCodes.Ldc_I4_1),
                                new CodeMatch(OpCodes.Add),
                                new CodeMatch(OpCodes.Stfld));
            
            // Assertion for patch success
            if (matcher.Remaining == 0)
            {
	            _instance.Logger.LogFatal("Could not find UpdatePlayerPositionClientRPC patch location, flagging abort!");
	            _patchFailed = true;
            }
	        
            matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0), // Patch in the reset of the player moving variable
                                    new CodeInstruction(OpCodes.Ldc_R4, 0f),
                                    new CodeInstruction(OpCodes.Stfld,
                                                        AccessTools.Field(typeof(PlayerControllerB), nameof(PlayerControllerB.timeSincePlayerMoving))));
	        
            return matcher.InstructionEnumeration();
        }
    }
}
