using Sandbox.ModAPI;
using System;
using VRage.Game;
using VRage.Game.Components;

namespace mz_00956.ImprovisedEngineering
{
    public struct ImprovisedConfig
    {
        public double flyingForce;
        public double groundForce;

        public double alignMaxAngularVelocity;
        public double alignMaxLinearVelocity;
        public double alignMaxTorque;

        public double reach;
        public double throwForce;
        public double maxSize;

        public double lineDist;
        public int grabTimeout;

        public int smallGridBlockCount;
        public float smallGridBoostMulti;

        public float closeToPlayerBoost;

        public bool rotToggle;
        public bool keyboard2;
        public bool showModes;
        public bool? grabTool;
        public bool holdUi;
        public bool offsetHand;
        public bool lockUse;

        public bool debugMode;
        public int newsVer;

        // Keep this helper for older code (not serialized)
        public bool GrabToolSetting => grabTool ?? false;
    }

    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Config : MySessionComponentBase
    {
        private const string ConfigName = "config";
        private const string Extension = ".xml";
        private static string ConfigFileName => ConfigName + Extension;

        private static ImprovisedConfig improvisedConfig = Default();

        private static ImprovisedConfig Default()
        {
            return new ImprovisedConfig()
            {
                flyingForce = 10000,
                groundForce = 20000,

                alignMaxAngularVelocity = 180,
                alignMaxLinearVelocity = 20,
                alignMaxTorque = 1e7,

                reach = 8,
                throwForce = 100000,
                maxSize = 20,

                lineDist = 50,
                grabTimeout = 0,

                smallGridBlockCount = 30,
                smallGridBoostMulti = 4,
                closeToPlayerBoost = 5,

                rotToggle = true,
                keyboard2 = false,
                showModes = true,
                grabTool = null,
                holdUi = false,
                offsetHand = true,
                lockUse = true,

                debugMode = false,
                newsVer = 0,
            };
        }

        // ------------ Properties (NOT serialized) ------------

        public static double FlyingForce
        {
            get
            {
                return improvisedConfig.flyingForce;
            }

            set { if (improvisedConfig.flyingForce != value) { improvisedConfig.flyingForce = value; Save(); } }
        }

        public static double GroundForce
        {
            get
            {
                return improvisedConfig.groundForce;
            }

            set { if (improvisedConfig.groundForce != value) { improvisedConfig.groundForce = value; Save(); } }
        }

        public static double AlignMaxAngularVelocity
        {
            get
            {
                return improvisedConfig.alignMaxAngularVelocity;
            }

            set { if (improvisedConfig.alignMaxAngularVelocity != value) { improvisedConfig.alignMaxAngularVelocity = value; Save(); } }
        }

        public static double AlignMaxLinearVelocity
        {
            get
            {
                return improvisedConfig.alignMaxLinearVelocity;
            }

            set { if (improvisedConfig.alignMaxLinearVelocity != value) { improvisedConfig.alignMaxLinearVelocity = value; Save(); } }
        }

        public static double AlignMaxTorque
        {
            get
            {
                return improvisedConfig.alignMaxTorque;
            }

            set { if (improvisedConfig.alignMaxTorque != value) { improvisedConfig.alignMaxTorque = value; Save(); } }
        }

        public static double Reach
        {
            get
            {
                return improvisedConfig.reach;
            }

            set { if (improvisedConfig.reach != value) { improvisedConfig.reach = value; Save(); } }
        }

        public static double ThrowForce
        {
            get
            {
                return improvisedConfig.throwForce;
            }

            set { if (improvisedConfig.throwForce != value) { improvisedConfig.throwForce = value; Save(); } }
        }

        public static double MaxSize
        {
            get
            {
                return improvisedConfig.maxSize;
            }

            set { if (improvisedConfig.maxSize != value) { improvisedConfig.maxSize = value; Save(); } }
        }

        public static double LineDist
        {
            get
            {
                return improvisedConfig.lineDist;
            }

            set { if (improvisedConfig.lineDist != value) { improvisedConfig.lineDist = value; Save(); } }
        }

        public static int GrabTimeout
        {
            get
            {
                return improvisedConfig.grabTimeout;
            }

            set { if (improvisedConfig.grabTimeout != value) { improvisedConfig.grabTimeout = value; Save(); } }
        }

        public static int SmallGridBlockCount
        {
            get
            {
                return improvisedConfig.smallGridBlockCount;
            }

            set { if (improvisedConfig.smallGridBlockCount != value) { improvisedConfig.smallGridBlockCount = value; Save(); } }
        }

        public static float SmallGridBoostMulti
        {
            get
            {
                return improvisedConfig.smallGridBoostMulti;
            }

            set { if (improvisedConfig.smallGridBoostMulti != value) { improvisedConfig.smallGridBoostMulti = value; Save(); } }
        }

        public static float CloseToPlayerBoost
        {
            get
            {
                return improvisedConfig.closeToPlayerBoost;
            }

            set { if (improvisedConfig.closeToPlayerBoost != value) { improvisedConfig.closeToPlayerBoost = value; Save(); } }
        }

        public static bool RotToggle
        {
            get
            {
                return improvisedConfig.rotToggle;
            }

            set { if (improvisedConfig.rotToggle != value) { improvisedConfig.rotToggle = value; Save(); } }
        }

        public static bool Keyboard2
        {
            get
            {
                return improvisedConfig.keyboard2;
            }

            set { if (improvisedConfig.keyboard2 != value) { improvisedConfig.keyboard2 = value; Save(); } }
        }

        public static bool ShowModes
        {
            get
            {
                return improvisedConfig.showModes;
            }

            set { if (improvisedConfig.showModes != value) { improvisedConfig.showModes = value; Save(); } }
        }

        public static bool? GrabTool
        {
            get
            {
                return improvisedConfig.grabTool;
            }

            set { if (improvisedConfig.grabTool != value) { improvisedConfig.grabTool = value; Save(); } }
        }

        public static bool GrabToolSetting => improvisedConfig.grabTool ?? false;

        public static bool HoldUi
        {
            get
            {
                return improvisedConfig.holdUi;
            }

            set { if (improvisedConfig.holdUi != value) { improvisedConfig.holdUi = value; Save(); } }
        }

        public static bool OffsetHand
        {
            get
            {
                return improvisedConfig.offsetHand;
            }

            set { if (improvisedConfig.offsetHand != value) { improvisedConfig.offsetHand = value; Save(); } }
        }

        public static bool LockUse
        {
            get
            {
                return improvisedConfig.lockUse;
            }

            set { if (improvisedConfig.lockUse != value) { improvisedConfig.lockUse = value; Save(); } }
        }

        public static bool DebugMode
        {
            get
            {
                return improvisedConfig.debugMode;
            }

            set { if (improvisedConfig.debugMode != value) { improvisedConfig.debugMode = value; Save(); } }
        }

        public static int NewsVer
        {
            get
            {
                return improvisedConfig.newsVer;
            }

            set { if (improvisedConfig.newsVer != value) { improvisedConfig.newsVer = value; Save(); } }
        }

        // ------------ Lifecycle ------------

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInWorldStorage(ConfigFileName, typeof(ImprovisedConfig)))
                {
                    var textReader = MyAPIGateway.Utilities.ReadFileInWorldStorage(ConfigFileName, typeof(ImprovisedConfig));
                    var configXml = textReader.ReadToEnd();
                    textReader.Close();
                    if (configXml.Length == 0)
                    {
                        Save();
                        return;
                    }

                    try
                    {
                        var tempConfig = MyAPIGateway.Utilities.SerializeFromXML<ImprovisedConfig>(configXml);
                        if (tempConfig.flyingForce == 0 && tempConfig.groundForce == 0 && tempConfig.throwForce == 0) throw new Exception("invalidValues");
                        improvisedConfig = tempConfig;
                    } 
                    catch
                    {
                        Debug.Error(message: "Config Init: Failed to parse config XML, resetting to defaults.", informUser: true);
                        try
                        {
                            using (var textWriter = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigName + ".corrupted" + DateTime.Now.ToString("yyyymmdd_HHmmfff") + Extension, typeof(ImprovisedConfig)))
                            {
                                textWriter.Write(configXml);
                                textWriter.Flush();
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.Error(message: "Config Backup Exception: " + e, informUser: true);
                        }
                        improvisedConfig = Default();
                        Save();
                    }
                    
                }
                else
                {
                    Save();
                }
            }
            catch (Exception e)
            {
                Debug.Error(message: "Config Init Exception: " + e, informUser: true);
            }
        }

        // ------------ Centralized Save ------------

        private static void Save()
        {
            try
            {
                using (var textWriter = MyAPIGateway.Utilities.WriteFileInWorldStorage(ConfigFileName, typeof(ImprovisedConfig)))
                {
                    textWriter.Write(MyAPIGateway.Utilities.SerializeToXML(improvisedConfig));
                    textWriter.Flush();
                }
            }
            catch (Exception e)
            {
                Debug.Error(message: "Config Save Exception: " + e, informUser: true);
            }
        }
    }
}
