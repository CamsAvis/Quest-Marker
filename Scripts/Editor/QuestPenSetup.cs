#if UNITY_EDITOR && VRC_SDK_VRCSDK3

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using System.Reflection;

namespace Cam
{
    static class PenSystem
    {
        public static List<string> layers = new List<string> {
            "Pen", "Pen Color"
        };

        public static List<string> parameters = new List<string> {
            "Pen/Rainbow", "Pen/Enable", "Pen/Color"
        };

        public static string menuName = "Quest Pen";
    }


    public class QuestPenSetup : EditorWindow
    {
        const string QUEST_MARKER_FOLDER = "Assets/!Cam/Quest Marker";
        const string GENERATED_ASSETS_FOLDER = "Assets/!Cam/Quest Marker/Generated Assets";
        const string PREFAB_NAME = "Pen";
        const string MENU_PATH = "Assets/!Cam/Quest Marker/Menu.asset";
        const string PARAM_PATH = "Assets/!Cam/Quest Marker/Param.asset";

        const string R_PREFAB_PATH = "Cam/Quest Pen/Pen";
        const string R_FX_BASE_PATH = "Cam/Quest Pen/FX";
        const string R_PEN_ICON_PATH = "Cam/Quest Pen/Icons/Pen";

        enum HandMode
        {
            RightHand,
            LeftHand
        }

        GameObject avatar;
        bool writeDefaults;
        bool mergeWithAvatar;
        HandMode handMode;
        Texture2D penIcon;

        MethodInfo mergeControllers;
        MethodInfo mergeToLayer;
        MethodInfo addSubMenu;
        MethodInfo addParameter;


        [MenuItem("Cam/Quest Pen")]
        public static void showWindow()
        {
            QuestPenSetup fm = GetWindow<QuestPenSetup>(false, "Cam's Quest Pen Installer", true);
            fm.minSize = new Vector2(430, 300);
        }

        private void OnEnable()
        {
            avatar = null;
            writeDefaults = true;
            handMode = HandMode.RightHand;
            penIcon = Resources.Load<Texture2D>(R_PEN_ICON_PATH);
            CheckVRLabsManager();
        }

        private void OnGUI()
        {
            GUILayout.Label("Cam's Quest Pen Installer", EditorStyles.whiteLargeLabel);

            if (mergeControllers == null)
            {
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.HelpBox("Cannot run script - AV3Manager not detected." +
                        "\nPlease click 'fix' to download the latest version of AV3Manager.",
                        MessageType.Error
                    );
                    if (GUILayout.Button("Fix", GUILayout.Height(38)))
                        Application.OpenURL("https://github.com/VRLabs/Avatars-3.0-Manager/releases");
                }

                // check again
                CheckVRLabsManager();
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    writeDefaults = EditorGUILayout.Toggle("Write Defaults?", writeDefaults);
                    mergeWithAvatar = EditorGUILayout.Toggle("Merge With Avatar?", mergeWithAvatar);
                }

                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    GUILayout.Label("Avatar", GUILayout.Width(50));
                    avatar = (GameObject)EditorGUILayout.ObjectField(avatar, typeof(GameObject), true);
                    handMode = (HandMode)EditorGUILayout.EnumPopup(handMode, GUILayout.Width(90));
                }
            }

            GUILayout.Space(5);

            using (new EditorGUI.DisabledGroupScope(avatar == null))
            {
                GUI.color = avatar == null ? Color.white : Color.green;
                if (GUILayout.Button(mergeWithAvatar ? "Install" : "Generate", GUILayout.Height(25)))
                    GenerateMarker();

                GUI.color = avatar == null ? Color.white : Color.red;
                if (GUILayout.Button("Remove", GUILayout.Height(25)))
                    DeleteMarker();

                GUI.color = Color.white;
            }

        }

        void CheckVRLabsManager()
        {
            Type animatorCloner = System.Type.GetType("VRLabs.AV3Manager.AnimatorCloner");
            if (animatorCloner == null)
            {
                Debug.LogError("VRLabs AnimatorCloner class not found - has VRLabs AV3Manager been imported?");
            }
            else
            {
                mergeControllers = animatorCloner.GetMethod("MergeControllers");
            }

            Type managerFunctions = System.Type.GetType("VRLabs.AV3Manager.AV3ManagerFunctions");
            if (managerFunctions == null)
            {
                Debug.LogError("VRLabs AV3ManagerFunctions class not found - has VRLabs AV3Manager been imported?");
            }
            else
            {
                mergeToLayer = managerFunctions.GetMethod("MergeToLayer");
                addSubMenu = managerFunctions.GetMethod("AddSubMenu");
                addParameter = managerFunctions.GetMethod("AddParameter");
            }
        }

        void GenerateMarker()
        {
            Animator a = avatar.GetComponent<Animator>();
            if (a == null)
            {
                Debug.LogError("Error - Avatar does not have an animator");
                return;
            }

            // generate folders
            if (!AssetDatabase.IsValidFolder(GENERATED_ASSETS_FOLDER))
                AssetDatabase.CreateFolder(QUEST_MARKER_FOLDER, "Generated Assets");

            string directory = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(GENERATED_ASSETS_FOLDER, avatar.name));
            string path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/FX.controller");

            // get pen and target
            GameObject penPrefab = Resources.Load<GameObject>(R_PREFAB_PATH);
            Transform target = a.GetBoneTransform(
                handMode == HandMode.RightHand ? HumanBodyBones.RightIndexDistal : HumanBodyBones.LeftIndexDistal
            );

            // setup pen prefab
            penPrefab = GameObject.Instantiate(penPrefab);
            if (PrefabUtility.IsPartOfPrefabAsset(penPrefab))
                PrefabUtility.UnpackPrefabInstance(penPrefab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            penPrefab.name = PREFAB_NAME;
            penPrefab.transform.SetParent(target);
            penPrefab.transform.localPosition = Vector3.zero;
            penPrefab.transform.localRotation = Quaternion.identity;

            // create files
            string targetPath = penPrefab.transform.GetHierarchyPath(avatar.transform);
            AnimatorController newController = GenerateControllerAndReturn(path);
            AnimationClip penClip = CreatePenClip(targetPath, directory);
            AnimationClip colorClip = CreatePenColorClip(targetPath, directory);
            AnimationClip whiteClip = CreatePenWhiteClip(targetPath, directory);

            // merge with controller
            string penControllerPath = $"{R_FX_BASE_PATH} {handMode.ToString().Replace("Hand", "")}";
            AnimatorController penController = Resources.Load<AnimatorController>(penControllerPath);
            mergeControllers.Invoke(null, new object[] {
                newController, penController, null, false
            });

            AnimatorControllerLayer penLayer = newController.layers[0];
            penLayer.avatarMask = writeDefaults ? null : penLayer.avatarMask;
            for (int i = 0; i < penLayer.stateMachine.states.Length; i++)
            {
                penLayer.stateMachine.states[i].state.writeDefaultValues = writeDefaults;
                penLayer.stateMachine.states[i].state.motion = penClip;
            }

            AnimatorControllerLayer colorLayer = newController.layers[1];
            colorLayer.avatarMask = writeDefaults ? null : colorLayer.avatarMask;
            for (int i = 0; i < colorLayer.stateMachine.states.Length; i++)
            {
                AnimatorState state = colorLayer.stateMachine.states[i].state;
                state.writeDefaultValues = writeDefaults;
                switch (state.name)
                {
                    case "Pen White":
                        state.motion = whiteClip;
                        break;
                    case "Rainbow":
                    case "Pen Color":
                        state.motion = colorClip;
                        break;
                }
            }
            newController.layers = new AnimatorControllerLayer[] { penLayer, colorLayer };

            // merge descriptor with pen in case it exists
            if (mergeWithAvatar)
            {
                MergePenWithAvatar(newController, directory);
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(newController));
            }
            else
            {
                EditorGUIUtility.PingObject(newController);
            }
        }

        void MergePenWithAvatar(AnimatorController controller, string directory)
        {
            VRCAvatarDescriptor descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) {
                Debug.LogWarning("Cannot merge with avatar - no descriptor has been detected");
                return;
            }

            directory += "/";

            // merge with FX layer
            mergeToLayer.Invoke(null, new object[] {
                descriptor, controller, 4, directory, true
            });

            // merge parameters
            VRCExpressionParameters parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(PARAM_PATH);
            for (int i = 0; i < parameters.parameters.Length; i++)
            {
                addParameter.Invoke(null, new object[] {
                    descriptor, parameters.parameters[i], directory, true
                });
            }
            
            // merge menu
            VRCExpressionsMenu subMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(MENU_PATH);
            addSubMenu.Invoke(null, new object[] {
                descriptor, subMenu, PenSystem.menuName, directory, null, penIcon, true
            });
        }

        void DeleteMarker()
        {
            VRCAvatarDescriptor descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
                return;

            // remove from animator
            RuntimeAnimatorController rac = descriptor.baseAnimationLayers[4].animatorController;
            if (rac != null)
            {
                AnimatorController controller = (AnimatorController)rac;
                EditorUtility.SetDirty(controller);
                controller.layers = controller.layers.Where(
                    layer => !PenSystem.layers.Contains(layer.name)
                ).ToArray();

                controller.parameters = controller.parameters.Where(
                    parameter => !PenSystem.parameters.Contains(parameter.name)
                ).ToArray();
            }

            // remove from menu
            if (descriptor.expressionsMenu != null)
            {
                VRCExpressionsMenu avatarMenu = descriptor.expressionsMenu;
                EditorUtility.SetDirty(avatarMenu);

                VRCExpressionsMenu penParentMenu;
                VRCExpressionsMenu.Control penControl;
                (penParentMenu, penControl) = GetPenParentMenu(descriptor.expressionsMenu);
                if (penParentMenu != null && penControl != null)
                {
                    penParentMenu.controls.Remove(penControl);
                }

                descriptor.expressionsMenu = avatarMenu;
            }

            // remove from parameters
            if (descriptor.expressionParameters != null)
            {
                VRCExpressionParameters parameters = descriptor.expressionParameters;
                EditorUtility.SetDirty(parameters);

                descriptor.expressionParameters.parameters = descriptor.expressionParameters.parameters.Where(
                    p => !PenSystem.parameters.Contains(p.name)
                ).ToArray();

                descriptor.expressionParameters = parameters;
            }

            // remove object from avatar
            Animator a = avatar.GetComponent<Animator>();
            if (a != null)
            {
                Transform fingerL = a.GetBoneTransform(HumanBodyBones.LeftIndexDistal);
                if (fingerL != null)
                    DestroyImmediate(fingerL.Find("Pen"));

                Transform fingerR = a.GetBoneTransform(HumanBodyBones.RightIndexDistal);
                if (fingerR != null)
                    DestroyImmediate(fingerR.Find("Pen"));
            }
        }

        (VRCExpressionsMenu, VRCExpressionsMenu.Control) GetPenParentMenu(VRCExpressionsMenu menu)
        {
            if (menu != null)
            {
                for (int i = 0; i < menu.controls.Count; i++)
                {
                    VRCExpressionsMenu.Control curCtrl = menu.controls[i];
                    if (curCtrl.name.Equals(PenSystem.menuName) && curCtrl.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        return (menu, curCtrl);
                    }
                    else if (curCtrl.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                    {
                        return GetPenParentMenu(curCtrl.subMenu);
                    }
                }
            }

            return (null, null);
        }

        static AnimationClip CreatePenWhiteClip(string transformPath, string directory)
        {
            AnimationClip clip = new AnimationClip();
            EditorUtility.SetDirty(clip);

            clip.wrapMode = WrapMode.Loop;
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._EmissionColor.r", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0  },
                new Keyframe() { time = 0.01f, value=1, inTangent=0, outTangent=0  },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._EmissionColor.g", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0  },
                new Keyframe() { time = 0.01f, value=1, inTangent=0, outTangent=0  },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._EmissionColor.b", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0  },
                new Keyframe() { time = 0.01f, value=1, inTangent=0, outTangent=0  },
            }
            });

            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._Color.r", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0  },
                new Keyframe() { time = 0.01f, value=1, inTangent=0, outTangent=0  },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._Color.g", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0  },
                new Keyframe() { time = 0.01f, value=1, inTangent=0, outTangent=0  },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._Color.b", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0  },
                new Keyframe() { time = 0.01f, value=1, inTangent=0, outTangent=0  },
            }
            });
            AssetDatabase.CreateAsset(clip, $"{directory}/Pen White.anim");
            return clip;
        }

        static AnimationClip CreatePenClip(string transformPath, string directory)
        {
            AnimationClip clip = new AnimationClip();
            EditorUtility.SetDirty(clip);
            clip.SetCurve(transformPath, typeof(TrailRenderer), "m_Emitting", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 0.1f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 0.2f, value=1, inTangent=0, outTangent=0 },
            },
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "m_Time", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 0.1f, value=9999, inTangent=0, outTangent=0 },
                new Keyframe() { time = 0.2f, value=9999, inTangent=0, outTangent=0 },
            }
            });
            AssetDatabase.CreateAsset(clip, $"{directory}/Pen.anim");
            return clip;
        }

        static AnimationClip CreatePenColorClip(string transformPath, string directory)
        {
            AnimationClip clip = new AnimationClip();
            EditorUtility.SetDirty(clip);

            clip.wrapMode = WrapMode.Loop;

            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._EmissionColor.r", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 5/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 10/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 15/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 20/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 25/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 30/60f, value=1, inTangent=0, outTangent=0 },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._EmissionColor.g", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 5/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 10/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 15/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 20/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 25/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 30/60f, value=0, inTangent=0, outTangent=0 },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._EmissionColor.b", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 5/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 10/60f, value=0 },
                new Keyframe() { time = 15/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 20/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 25/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 30/60f, value=0, inTangent=0, outTangent=0 },
            }
            });

            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._Color.r", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 5/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 10/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 15/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 20/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 25/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 30/60f, value=1, inTangent=0, outTangent=0 },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._Color.g", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 5/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 10/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 15/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 20/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 25/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 30/60f, value=0, inTangent=0, outTangent=0 },
            }
            });
            clip.SetCurve(transformPath, typeof(TrailRenderer), "material._Color.b", new AnimationCurve()
            {
                keys = new Keyframe[] {
                new Keyframe() { time = 0, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 5/60f, value=0, inTangent=0, outTangent=0 },
                new Keyframe() { time = 10/60f, value=0 },
                new Keyframe() { time = 15/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 20/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 25/60f, value=1, inTangent=0, outTangent=0 },
                new Keyframe() { time = 30/60f, value=0, inTangent=0, outTangent=0 },
            }
            });

            AssetDatabase.CreateAsset(clip, $"{directory}/Pen Color.anim");
            return clip;
        }

        static AnimatorController GenerateControllerAndReturn(string path)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.layers = new AnimatorControllerLayer[0];
            return controller;
        }
    }
}
#endif