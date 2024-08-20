using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine.UIElements;

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
	        // Create the local variable to hold the OR state
	        //LocalBuilder orConditionOne = generator.DeclareLocal(typeof(bool));

	        // Transpile!
	        CodeMatcher matcher = new (instructions);
	        matcher.Start();
	        
	        // 1. Get the op codes for checking the current user's time to move.
	        matcher.MatchForward(false, // Find the check to copy and modify.
	                          new CodeMatch(OpCodes.Call),
	                          new CodeMatch(OpCodes.Ldfld),
	                          new CodeMatch(OpCodes.Ldloc_S),
	                          new CodeMatch(OpCodes.Ldelem_Ref),
	                          new CodeMatch(OpCodes.Ldfld),
	                          new CodeMatch(OpCodes.Ldc_R4));
	        
	        // Assertion for patch success
	        if (matcher.Remaining == 0)
	        {
		        _instance.Logger.LogFatal("Could not find LookForPlayers time since moved code block to patch, flagging to abort!");
		        _patchFailed = true;
	        }

	        // 2. Get the op codes for check against the player's move time, and configure the time.
	        List<CodeInstruction> checkPlayerTimeCodes = new();
	        for (int i = 0; i < 6; i++)
	        {
		        CodeInstruction copiedInstruction = new (matcher.Instruction);
		        checkPlayerTimeCodes.Add(copiedInstruction);
		        matcher.Advance(1);
	        }
	        // Set the time to check against.
	        checkPlayerTimeCodes[^1].operand = _configMoveTime.Value;
	        // Add the comparison operator.
	        checkPlayerTimeCodes.Add(new CodeInstruction(OpCodes.Clt));
            
            // 3. Move to the IF condition.
            matcher.Start();
            matcher.MatchForward(true,
                                 new CodeMatch(OpCodes.Ldloc_0),
                                 new CodeMatch(OpCodes.Call),
                                 new CodeMatch(OpCodes.Ldfld),
                                 new CodeMatch(OpCodes.Ldloc_S),
                                 new CodeMatch(OpCodes.Ldelem_Ref),
                                 new CodeMatch(OpCodes.Call),
                                 new CodeMatch(OpCodes.Brfalse));
            
            // Assertion for patch success.
            if (matcher.Remaining == 0)
            {
	            _instance.Logger.LogFatal("Could not find LookForPlayers IF condition to patch, flagging to abort!");
	            _patchFailed = true;
	            return matcher.InstructionEnumeration();
            }
            
            // 4. Insert the check against the player's move time.
            checkPlayerTimeCodes.Add(new CodeInstruction(matcher.Instruction)); // Copy the branch instruction.
            matcher.Advance(1);
            matcher.InsertAndAdvance(checkPlayerTimeCodes);
            
            // Return the finished instructions
            return matcher.InstructionEnumeration();
        }

        // I think this bug got patched!
        //[HarmonyPatch(typeof(PlayerControllerB), "UpdatePlayerPositionClientRpc")]
        //[HarmonyTranspiler]
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
