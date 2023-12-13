using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;

namespace ForestGiantMotionsense
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class FoGiMoSeMod : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
            Harmony.CreateAndPatchAll(typeof(FoGiMoSeMod));
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
            // Store the branch target instruction
            Label labelBranchTarget = matcher.Instruction.labels.First(); // Create the branch target
            matcher.MatchBack(false,
                            new CodeMatch(OpCodes.Ldarg_0),
                            new CodeMatch(OpCodes.Ldfld),
                            new CodeMatch(OpCodes.Ldloc_S),
                            new CodeMatch(OpCodes.Ldelem_R4),
                            new CodeMatch(OpCodes.Ldloc_S),
                            new CodeMatch(OpCodes.Ble_Un));
            
            // Cache the load ops
            CodeInstruction loadIndexTwo = matcher.Advance(2).Instruction;
            CodeInstruction loadNum2 = matcher.Advance(2).Instruction;
            matcher.Advance(-4); // Correct the position back to the start of the removed block
            
            // Clear the old opcodes out
            matcher.RemoveInstructions(6)                     // Cut out the old instructions
                   .InsertAndAdvance(                         // __ First AND Condition
                        new CodeInstruction(OpCodes.Ldarg_0), // Load 'this'
                        new CodeInstruction(OpCodes.Ldfld,    // Load the array
                                            typeof(ForestGiantAI).GetField("playerStealthMeters", BindingFlags.Public | BindingFlags.Instance)),
                        loadIndexTwo,                           // Load the current player index
                        new CodeInstruction(OpCodes.Ldelem_R4), // Retrieve the current player's stealth value
                        loadNum2,                               // Load the worst stealth value
                        new CodeInstruction(OpCodes.Cgt),       // Compare the current stealth vs the worst
                        new CodeInstruction(OpCodes.Stloc_S,    // Store the first AND value
                                            andConditionOne),
                        // __ Second AND Condition
                        // First OR Condition
                        new CodeInstruction(OpCodes.Call, // Get the singleton reference of the "game manager"
                                            typeof(StartOfRound).GetMethod("get_Instance", BindingFlags.Public | BindingFlags.Static)),
                        new CodeInstruction(OpCodes.Ldfld, // Load the player array
                                            typeof(StartOfRound).GetField("allPlayerScripts", BindingFlags.Public | BindingFlags.Instance)),
                        loadIndexTwo,                            // Load the index of the current player
                        new CodeInstruction(OpCodes.Ldelem_Ref), // Get the reference to the current player from the array
                        new CodeInstruction(OpCodes.Dup),        // Duplicate the reference
                        new CodeInstruction(OpCodes.Ldfld,       // Load the time since moving field
                                            typeof(GameNetcodeStuff.PlayerControllerB).GetField("timeSincePlayerMoving", BindingFlags.Public | BindingFlags.Instance)),
                        new CodeInstruction(OpCodes.Ldc_R4, 2.25f),           // Load the movement duration sensitivity
                        new CodeInstruction(OpCodes.Clt),                     // Check if the time since the  player's movement is less than the sensitivity threshold
                        new CodeInstruction(OpCodes.Stloc_S, orConditionOne), // Store the first OR condition
                        // Second OR Condition
                        // We already have the player reference on the stack
                        new CodeInstruction(OpCodes.Ldarg_0), // Get a pointer for 'this' forest giant
                        new CodeInstruction(OpCodes.Ldfld,    // Get the chasingPlayer on 'this' forest giant
                                            typeof(ForestGiantAI).GetField("chasingPlayer", BindingFlags.Public | BindingFlags.Instance)),
                        new CodeInstruction(OpCodes.Call, typeof(UnityEngine.Object).GetMethod("op_Equality")), // Compare the two players
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
            return new CodeMatcher(instructions)
                  .MatchForward(true,
                                new CodeMatch(OpCodes.Ldarg_0), 
                                new CodeMatch(OpCodes.Ldfld),
                                new CodeMatch(OpCodes.Ldfld),
                                new CodeMatch(OpCodes.Dup),
                                new CodeMatch(OpCodes.Ldfld),
                                new CodeMatch(OpCodes.Ldc_I4_1),
                                new CodeMatch(OpCodes.Add),
                                new CodeMatch(OpCodes.Stfld))
                  .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0), // Patch in the reset of the player moving variable
                                    new CodeInstruction(OpCodes.Ldc_R4, 0f),
                                    new CodeInstruction(OpCodes.Stfld, 
                                                        typeof(PlayerControllerB).GetField("timeSincePlayerMoving", 
                                                                                           BindingFlags.Public | BindingFlags.Instance)))
                  .InstructionEnumeration();
        }
    }
}
