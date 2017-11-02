using UnityEngine;

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;

using V1=AssetBundleGraph;
using Model=UnityEngine.AssetGraph.DataModel.Version2;

namespace UnityEngine.AssetGraph {

	[CustomNode("Load Assets/Load From Directory", 10)]
	public class Loader : Node, Model.NodeDataImporter {

        [SerializeField] private SerializableMultiTargetString m_loadPath;
        [SerializeField] private SerializableMultiTargetString m_loadPathGuid;

		public override string ActiveStyle {
			get {
				return "node 0 on";
			}
		}

		public override string InactiveStyle {
			get {
				return "node 0";
			}
		}

		public override string Category {
			get {
				return "Load";
			}
		}
			
		public override Model.NodeOutputSemantics NodeInputType {
			get {
				return Model.NodeOutputSemantics.None;
			}
		}

        public Loader() {}
        public Loader(string path) {
            var normalizedPath = NormalizeLoadPath (path);
            var loadPath = FileUtility.PathCombine (Model.Settings.Path.ASSETS_PATH, normalizedPath);
            var guid = AssetDatabase.AssetPathToGUID (loadPath);

            m_loadPath = new SerializableMultiTargetString(normalizedPath);
            m_loadPathGuid = new SerializableMultiTargetString (guid);
        }

		public override void Initialize(Model.NodeData data) {
            if (m_loadPath == null) {
                m_loadPath = new SerializableMultiTargetString();
            }
            if (m_loadPathGuid == null) {
                m_loadPathGuid = new SerializableMultiTargetString();
            }

			data.AddDefaultOutputPoint();
		}

		public void Import(V1.NodeData v1, Model.NodeData v2) {
			m_loadPath = new SerializableMultiTargetString(v1.LoaderLoadPath);
            m_loadPathGuid = new SerializableMultiTargetString();
            foreach (var v in m_loadPath.Values) {
                var loadPath = FileUtility.PathCombine (Model.Settings.Path.ASSETS_PATH, v.value);
                m_loadPathGuid [v.targetGroup] = AssetDatabase.AssetPathToGUID (loadPath);
            }
		}

		public override Node Clone(Model.NodeData newData) {
			var newNode = new Loader();
            newNode.m_loadPath = new SerializableMultiTargetString(m_loadPath);
            newNode.m_loadPathGuid = new SerializableMultiTargetString(m_loadPathGuid);

			newData.AddDefaultOutputPoint();
			return newNode;
		}

        private void CheckAndCorrectPath(BuildTarget target) {
            var loadPath = GetLoadPath (target);
            var pathFromGuid = AssetDatabase.GUIDToAssetPath (m_loadPathGuid[target]);

            // fix load path from guid (adopting folder rename)
            if (!AssetDatabase.IsValidFolder (loadPath)) {
                if (!string.IsNullOrEmpty (pathFromGuid)) {
                    if (m_loadPath.ContainsValueOf (target)) {
                        m_loadPath [target] = NormalizeLoadPath (pathFromGuid);
                    } else {
                        m_loadPath.DefaultValue = NormalizeLoadPath (pathFromGuid);
                    }
                }
            } 
            // if folder is valid and guid is invalid, reflect folder to guid
            else {
                if (string.IsNullOrEmpty (pathFromGuid)) {
                    if (m_loadPath.ContainsValueOf (target)) {
                        m_loadPathGuid [target] = AssetDatabase.AssetPathToGUID (loadPath);
                    } else {
                        m_loadPathGuid.DefaultValue = AssetDatabase.AssetPathToGUID (loadPath);
                    }
                }
            }
        }

		public override bool OnAssetsReimported(
			Model.NodeData nodeData,
			AssetReferenceStreamManager streamManager,
			BuildTarget target, 
			string[] importedAssets, 
			string[] deletedAssets, 
			string[] movedAssets, 
			string[] movedFromAssetPaths)
		{
			if (streamManager == null) {
				return true;
			}

            CheckAndCorrectPath (target);

			var loadPath = m_loadPath[target];
			// if loadPath is null/empty, loader load everything except for settings
			if(string.IsNullOrEmpty(loadPath)) {
				// ignore config file path update
                var notConfigFilePath = importedAssets.Where(path => !TypeUtility.IsAssetGraphSystemAsset(path));
                if(notConfigFilePath.Any()) {
					LogUtility.Logger.LogFormat(LogType.Log, "{0} is marked to revisit", nodeData.Name);
					return true;
				}
			}

			var assetGroup = streamManager.FindAssetGroup(nodeData.OutputPoints[0]);

			if( assetGroup.Count > 0 ) {
                var importPath = GetLoadPath (target);

				foreach(var path in importedAssets) {
					if(path.StartsWith(importPath)) {
						// if this is reimport, we don't need to redo Loader
						if ( assetGroup["0"].Find(x => x.importFrom == path) == null ) {
							LogUtility.Logger.LogFormat(LogType.Log, "{0} is marked to revisit", nodeData.Name);
							return true;
						}
					}
				}
				foreach(var path in deletedAssets) {
					if(path.StartsWith(importPath)) {
						LogUtility.Logger.LogFormat(LogType.Log, "{0} is marked to revisit", nodeData.Name);
						return true;
					}
				}
				foreach(var path in movedAssets) {
					if(path.StartsWith(importPath)) {
						LogUtility.Logger.LogFormat(LogType.Log, "{0} is marked to revisit", nodeData.Name);
						return true;
					}
				}
				foreach(var path in movedFromAssetPaths) {
					if(path.StartsWith(importPath)) {
						LogUtility.Logger.LogFormat(LogType.Log, "{0} is marked to revisit", nodeData.Name);
						return true;
					}
				}
			}
			return false;
		}

        public static string NormalizeLoadPath(string path) {
            if(!string.IsNullOrEmpty(path)) {
                var dataPath = Application.dataPath;
                if(dataPath == path) {
                    path = string.Empty;
                } else {
                    var index = path.IndexOf(dataPath);
                    if (index >= 0) {
                        path = path.Substring (dataPath.Length + index);
                        if (path.IndexOf ('/') == 0) {
                            path = path.Substring (1);
                        }
                    } else if(path.StartsWith (Model.Settings.Path.ASSETS_PATH)) {
                        path = path.Substring (Model.Settings.Path.ASSETS_PATH.Length);
                    }
                }
            }
            return path;
        }

		public override void OnInspectorGUI(NodeGUI node, AssetReferenceStreamManager streamManager, NodeGUIEditor editor, Action onValueChanged) {

			if (m_loadPath == null) {
				return;
			}

			EditorGUILayout.HelpBox("Load From Directory: Load assets from given directory path.", MessageType.Info);
			editor.UpdateNodeName(node);

			GUILayout.Space(10f);

			//Show target configuration tab
			editor.DrawPlatformSelector(node);
			using (new EditorGUILayout.VerticalScope(GUI.skin.box)) {
				var disabledScope = editor.DrawOverrideTargetToggle(node, m_loadPath.ContainsValueOf(editor.CurrentEditingGroup), (bool b) => {
					using(new RecordUndoScope("Remove Target Load Path Settings", node, true)) {
						if(b) {
                            m_loadPath[editor.CurrentEditingGroup] = m_loadPath.DefaultValue;
                            m_loadPathGuid[editor.CurrentEditingGroup] = m_loadPathGuid.DefaultValue;
						} else {
							m_loadPath.Remove(editor.CurrentEditingGroup);
                            m_loadPathGuid.Remove(editor.CurrentEditingGroup);
						}
						onValueChanged();
					}
				});

				using (disabledScope) {
					var path = m_loadPath[editor.CurrentEditingGroup];
					EditorGUILayout.LabelField("Load Path:");

					string newLoadPath = null;

                    newLoadPath = editor.DrawFolderSelector (Model.Settings.Path.ASSETS_PATH, "Select Asset Folder", 
                        path,
                        FileUtility.PathCombine(Model.Settings.Path.ASSETS_PATH, path),
                        (string folderSelected) => { return NormalizeLoadPath(folderSelected); }
                    );

                    var dirPath = Path.Combine(Model.Settings.Path.ASSETS_PATH,newLoadPath);

					if (newLoadPath != path) {
						using(new RecordUndoScope("Load Path Changed", node, true)){
							m_loadPath[editor.CurrentEditingGroup] = newLoadPath;
                            m_loadPathGuid [editor.CurrentEditingGroup] = AssetDatabase.AssetPathToGUID (dirPath);
							onValueChanged();
						}
					}

					bool dirExists = Directory.Exists(dirPath);

					GUILayout.Space(10f);

					using (new EditorGUILayout.HorizontalScope()) {
						using(new EditorGUI.DisabledScope(string.IsNullOrEmpty(newLoadPath)||!dirExists)) 
						{
							GUILayout.FlexibleSpace();
							if(GUILayout.Button("Highlight in Project Window", GUILayout.Width(180f))) {
								// trailing is "/" not good for LoadMainAssetAtPath
								if(dirPath[dirPath.Length-1] == '/') {
									dirPath = dirPath.Substring(0, dirPath.Length-1);
								}
								var obj = AssetDatabase.LoadMainAssetAtPath(dirPath);
								EditorGUIUtility.PingObject(obj);
							}
						}
					}

					if(!dirExists) {
						var parentDirPath = Path.GetDirectoryName(dirPath);
						bool parentDirExists = Directory.Exists(parentDirPath);
						if(parentDirExists) {
							EditorGUILayout.LabelField("Available Directories:");
							string[] dirs = Directory.GetDirectories(parentDirPath);
							foreach(string s in dirs) {
								EditorGUILayout.LabelField(s);
							}
						}
					}
				}
			}
		}


		public override void Prepare (BuildTarget target, 
			Model.NodeData node, 
			IEnumerable<PerformGraph.AssetGroups> incoming, 
			IEnumerable<Model.ConnectionData> connectionsToOutput, 
			PerformGraph.Output Output) 
		{
            CheckAndCorrectPath (target);

			ValidateLoadPath(
				m_loadPath[target],
				GetLoaderFullLoadPath(target),
				() => {
					//can be empty
					//throw new NodeException(node.Name + ": Load Path is empty.", node.Id);
				}, 
				() => {
					throw new NodeException(node.Name + ": Directory not found: " + GetLoaderFullLoadPath(target), node.Id);
				}
			);

			Load(target, node, connectionsToOutput, Output);
		}
		
		void Load (BuildTarget target, 
			Model.NodeData node, 
			IEnumerable<Model.ConnectionData> connectionsToOutput, 
			PerformGraph.Output Output) 
		{

			if(connectionsToOutput == null || Output == null) {
				return;
			}

			var outputSource = new List<AssetReference>();

            var loadPath = GetLoadPath (target);
            var targetFiles = AssetDatabase.FindAssets("",  new string[] { loadPath });

            foreach (var assetGuid in targetFiles) {
                var targetFilePath = AssetDatabase.GUIDToAssetPath (assetGuid);

                if (TypeUtility.IsAssetGraphSystemAsset (targetFilePath)) {
                    continue;
                }

                // Skip folders
                var type = AssetDatabase.GetMainAssetTypeAtPath (targetFilePath);
                if (type == typeof(UnityEditor.DefaultAsset) && AssetDatabase.IsValidFolder(targetFilePath) ) {
                    continue;
                }

                var r = AssetReferenceDatabase.GetReference(targetFilePath);

                if (r == null) {
                    continue;
                }

                if(!TypeUtility.IsLoadingAsset(r)) {
                    continue;
                }

                if (outputSource.Contains (r)) {
                    continue;
                }

                if(r != null) {
                    outputSource.Add(r);
                }
			}

			var output = new Dictionary<string, List<AssetReference>> {
				{"0", outputSource}
			};

			var dst = (connectionsToOutput == null || !connectionsToOutput.Any())? 
				null : connectionsToOutput.First();
			Output(dst, output);
		}

		public static void ValidateLoadPath (string currentLoadPath, string combinedPath, Action NullOrEmpty, Action NotExist) {
			if (string.IsNullOrEmpty(currentLoadPath)) NullOrEmpty();
			if (!Directory.Exists(combinedPath)) NotExist();
		}

        private string GetLoadPath(BuildTarget g) {
            var path = m_loadPath [g];
            if (string.IsNullOrEmpty (path)) {
                return "Assets";
            } else {
                return FileUtility.PathCombine (Model.Settings.Path.ASSETS_PATH, path);
            }
        }

		private string GetLoaderFullLoadPath(BuildTarget g) {
			return FileUtility.PathCombine(Application.dataPath, m_loadPath[g]);
		}
	}
}