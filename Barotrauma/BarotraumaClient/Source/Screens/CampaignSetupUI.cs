﻿using Barotrauma.Tutorials;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CampaignSetupUI
    {
        private GUIComponent newGameContainer, loadGameContainer;

        private GUIListBox subList;
        private GUIListBox saveList;

        private GUITextBox saveNameBox, seedBox;
        private GUITickBox contextualTutorialBox;

        private GUILayoutGroup subPreviewContainer;

        private GUIButton loadGameButton;
        
        public Action<Submarine, string, string> StartNewGame;
        public Action<string> LoadGame;
        public bool TutorialSelected
        {
            get
            {
                if (contextualTutorialBox == null) return false;
                return contextualTutorialBox.Selected;
            }
        }

        private readonly bool isMultiplayer;

        public CampaignSetupUI(bool isMultiplayer, GUIComponent newGameContainer, GUIComponent loadGameContainer, IEnumerable<Submarine> submarines, IEnumerable<string> saveFiles = null)
        {
            this.isMultiplayer = isMultiplayer;
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;

            var columnContainer = new GUILayoutGroup(new RectTransform(Vector2.One, newGameContainer.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = isMultiplayer ? 0.0f : 0.05f
            };

            var leftColumn = new GUILayoutGroup(new RectTransform(Vector2.One, columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            var rightColumn = new GUILayoutGroup(new RectTransform(isMultiplayer ? Vector2.Zero : new Vector2(1.5f, 1.0f), columnContainer.RectTransform))
            {
                Stretch = true,
                RelativeSpacing = 0.015f
            };

            columnContainer.Recalculate();

            // New game left side
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SaveName") + ":");
            saveNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, string.Empty);

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("MapSeed") + ":");
            seedBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.05f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, ToolBox.RandomSeed(8));

            if (!isMultiplayer)
            {
                contextualTutorialBox = new GUITickBox(new RectTransform(new Point(32, 32), leftColumn.RectTransform), TextManager.Get("TutorialActive"));
                UpdateTutorialSelection();
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.02f), leftColumn.RectTransform) { MinSize = new Point(0, 20) }, TextManager.Get("SelectedSub") + ":");
            subList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.65f), leftColumn.RectTransform)) { ScrollBarVisible = true };

            if (!isMultiplayer) { subList.OnSelected = OnSubSelected; }

            // New game right side
            subPreviewContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.8f), rightColumn.RectTransform))
            {
                Stretch = true
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.13f), 
                (isMultiplayer ? leftColumn : rightColumn).RectTransform) { MaxSize = new Point(int.MaxValue, 60) }, childAnchor: Anchor.TopRight);

            var startButton = new GUIButton(new RectTransform(isMultiplayer ? new Vector2(0.5f, 1.0f) : Vector2.One,
                buttonContainer.RectTransform, Anchor.BottomRight) { MaxSize = new Point(350, 60) }, 
                TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
            {
                OnClicked = (GUIButton btn, object userData) =>
                {
                    if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                    {
                        saveNameBox.Flash(Color.Red);
                        return false;
                    }

                    if (!(subList.SelectedData is Submarine selectedSub)) { return false; }

                    if (string.IsNullOrEmpty(selectedSub.MD5Hash.Hash))
                    {
                        ((GUITextBlock)subList.SelectedComponent).TextColor = Color.DarkRed * 0.8f;
                        subList.SelectedComponent.CanBeFocused = false;
                        subList.Deselect();
                        return false;
                    }

                    string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer, saveNameBox.Text);
                    bool hasRequiredContentPackages = selectedSub.RequiredContentPackages.All(cp => GameMain.SelectedPackages.Any(cp2 => cp2.Name == cp));

                    if (selectedSub.HasTag(SubmarineTag.Shuttle) || !hasRequiredContentPackages)
                    {
                        if (!hasRequiredContentPackages)
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                                TextManager.Get("ContentPackageMismatchWarning")
                                    .Replace("[requiredcontentpackages]", string.Join(", ", selectedSub.RequiredContentPackages)),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                            msgBox.Buttons[0].OnClicked = msgBox.Close;
                            msgBox.Buttons[0].OnClicked += (button, obj) =>
                            {
                                if (GUIMessageBox.MessageBoxes.Count == 0)
                                {
                                    StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                                    if (isMultiplayer)
                                    {
                                        CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                                    }
                                }
                                return true;
                            };

                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                        }

                        if (selectedSub.HasTag(SubmarineTag.Shuttle))
                        {
                            var msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"),
                                TextManager.Get("ShuttleWarning"),
                                new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                            msgBox.Buttons[0].OnClicked = (button, obj) => 
                            {
                                StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                                if (isMultiplayer)
                                {
                                    CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                                }
                                return true;
                            };
                            msgBox.Buttons[0].OnClicked += msgBox.Close;

                            msgBox.Buttons[1].OnClicked = msgBox.Close;
                            return false;
                        }
                    }
                    else
                    {
                        StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                        if (isMultiplayer)
                        {
                            CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                        }
                    }

                    return true;
                }
            };

            leftColumn.Recalculate();
            rightColumn.Recalculate();


            UpdateSubList(submarines);
            UpdateLoadMenu(saveFiles);
        }

        public void RandomizeSeed()
        {
            seedBox.Text = ToolBox.RandomSeed(8);
        }

        private bool OnSubSelected(GUIComponent component, object obj)
        {
            if (subPreviewContainer == null) { return false; }

            subPreviewContainer.ClearChildren();

            Submarine sub = obj as Submarine;
            if (sub == null) { return true; }

            sub.CreatePreviewWindow(subPreviewContainer);
            return true;
        }

        private IEnumerable<object> WaitForCampaignSetup()
        {
            string headerText = TextManager.Get("CampaignStartingPleaseWait");
            var msgBox = new GUIMessageBox(headerText, TextManager.Get("CampaignStarting"), new string[] { TextManager.Get("Cancel") });

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                GameMain.NetLobbyScreen.SelectMode(0);
                CoroutineManager.StopCoroutines("WaitForCampaignSetup");
                return true;
            };
            msgBox.Buttons[0].OnClicked += msgBox.Close;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 10);
            while (GameMain.NetLobbyScreen.CampaignUI == null && DateTime.Now < timeOut)
            {
                msgBox.Header.Text = headerText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                yield return CoroutineStatus.Running;
            }
            msgBox.Close();
            yield return CoroutineStatus.Success;
        }

        public void CreateDefaultSaveName()
        {
            string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            saveNameBox.Text = Path.GetFileNameWithoutExtension(savePath);
        }

        public void UpdateSubList(IEnumerable<Submarine> submarines)
        {
#if DEBUG
            var subsToShow = submarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus));
#else
            var subsToShow = submarines;
#endif

            subList.ClearChildren();

            foreach (Submarine sub in subsToShow)
            {
                var textBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0.1f), subList.Content.RectTransform) { MinSize = new Point(0, 30) },
                    ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                    {
                        ToolTip = sub.Description,
                        UserData = sub
                    };
                               

                if (sub.HasTag(SubmarineTag.Shuttle))
                {
                    textBlock.TextColor = textBlock.TextColor * 0.85f;

                    var shuttleText = new GUITextBlock(new RectTransform(new Point(100, textBlock.Rect.Height), textBlock.RectTransform, Anchor.CenterRight)
                    {
                        IsFixedSize = false
                    },
                        TextManager.Get("Shuttle"), textAlignment: Alignment.Right, font: GUI.SmallFont)
                    {
                        TextColor = textBlock.TextColor * 0.8f,
                        ToolTip = textBlock.ToolTip
                    };
                }
            }
            if (Submarine.SavedSubmarines.Any())
            {
                var nonShuttles = subsToShow.Where(s => !s.HasTag(SubmarineTag.Shuttle)).ToList();
                if (nonShuttles.Count > 0)
                {
                    subList.Select(nonShuttles[Rand.Int(nonShuttles.Count)]);
                }
            }
        }

        public void UpdateLoadMenu(IEnumerable<string> saveFiles = null)
        {
            loadGameContainer.ClearChildren();

            if (saveFiles == null)
            {
                saveFiles = SaveUtil.GetSaveFiles(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            }

            saveList = new GUIListBox(new RectTransform(
                isMultiplayer ? new Vector2(1.0f, 0.85f) : new Vector2(0.5f, 1.0f), loadGameContainer.RectTransform))
            {
                OnSelected = SelectSaveFile
            };
            
            foreach (string saveFile in saveFiles)
            {
                string fileName = saveFile;
                string subName = "";
                string saveTime = "";
                var saveFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), saveList.Content.RectTransform) { MinSize = new Point(0, 45) }, style: "ListBoxElement")
                {
                    UserData = saveFile
                };

                var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform),
                    text: Path.GetFileNameWithoutExtension(saveFile));

                if (!isMultiplayer)
                {
                    XDocument doc = SaveUtil.LoadGameSessionDoc(saveFile);
                    if (doc?.Root == null)
                    {
                        DebugConsole.ThrowError("Error loading save file \"" + saveFile + "\". The file may be corrupted.");
                        nameText.Color = Color.Red;
                        continue;
                    }
                    subName =  doc.Root.GetAttributeString("submarine", "");
                    saveTime = doc.Root.GetAttributeString("savetime", "");
                }
                else
                {
                    string[] splitSaveFile = saveFile.Split(';');
                    saveFrame.UserData = splitSaveFile[0];
                    fileName = nameText.Text = Path.GetFileNameWithoutExtension(splitSaveFile[0]);
                    if (splitSaveFile.Length > 1) { subName = splitSaveFile[1]; }
                    if (splitSaveFile.Length > 2) { saveTime = splitSaveFile[2]; }
                }
                
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), saveFrame.RectTransform, Anchor.BottomLeft),
                    text: subName, font: GUI.SmallFont)
                {
                    UserData = fileName
                };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), saveFrame.RectTransform),
                    text: saveTime, textAlignment: Alignment.Right, font: GUI.SmallFont)
                {
                    UserData = fileName
                };
            }

            saveList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                string file1 = c1.GUIComponent.UserData as string;
                string file2 = c2.GUIComponent.UserData as string;
                DateTime file1WriteTime = DateTime.MinValue;
                DateTime file2WriteTime = DateTime.MinValue;
                try
                {
                    file1WriteTime = File.GetLastWriteTime(file1);
                }
                catch
                { 
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                try
                {
                    file2WriteTime = File.GetLastWriteTime(file2);
                }
                catch
                {
                    //do nothing - DateTime.MinValue will be used and the element will get sorted at the bottom of the list 
                };
                return file2WriteTime.CompareTo(file1WriteTime);
            });

            loadGameButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.12f), loadGameContainer.RectTransform, Anchor.BottomRight), TextManager.Get("LoadButton"), style: "GUIButtonLarge")
            {
                OnClicked = (btn, obj) =>
                {
                    if (string.IsNullOrWhiteSpace(saveList.SelectedData as string)) return false;
                    LoadGame?.Invoke(saveList.SelectedData as string);
                    if (isMultiplayer)
                    {
                        CoroutineManager.StartCoroutine(WaitForCampaignSetup(), "WaitForCampaignSetup");
                    }
                    return true;
                },
                Enabled = false
            };
        }

        public void UpdateTutorialSelection()
        {
            if (isMultiplayer) return;
            Tutorial contextualTutorial = Tutorial.Tutorials.Find(t => t is ContextualTutorial);
            contextualTutorialBox.Selected = (contextualTutorial != null) ? !GameMain.Config.CompletedTutorialNames.Contains(contextualTutorial.Name) : true;
        }
        
        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            if (isMultiplayer)
            {
                loadGameButton.Enabled = true;
                return true;
            }

            string fileName = (string)obj;

            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);
            if (doc == null)
            {
                DebugConsole.ThrowError("Error loading save file \"" + fileName + "\". The file may be corrupted.");
                return false;
            }

            loadGameButton.Enabled = true;

            RemoveSaveFrame();

            string subName = doc.Root.GetAttributeString("submarine", "");
            string saveTime = doc.Root.GetAttributeString("savetime", "unknown");
            string mapseed = doc.Root.GetAttributeString("mapseed", "unknown");

            var saveFileFrame = new GUIFrame(new RectTransform(new Vector2(0.45f, 0.6f), loadGameContainer.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.0f, 0.1f)
            }, style: "InnerFrame")
            {
                UserData = "savefileframe"
            };

            new GUITextBlock(new RectTransform(new Vector2(1, 0.2f), saveFileFrame.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, 0.05f)
            }, 
            Path.GetFileNameWithoutExtension(fileName), font: GUI.LargeFont, textAlignment: Alignment.Center);

            var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), saveFileFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            });

            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("Submarine")} : {subName}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("LastSaved")} : {saveTime}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("MapSeed")} : {mapseed}", font: GUI.SmallFont);

            new GUIButton(new RectTransform(new Vector2(0.4f, 0.15f), saveFileFrame.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("Delete"))
            {
                UserData = fileName,
                OnClicked = DeleteSave
            };

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;

            if (obj == null) return false;

            SaveUtil.DeleteSave(saveFile);

            UpdateLoadMenu();

            return true;
        }

        private void RemoveSaveFrame()
        {
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in loadGameContainer.Children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            loadGameContainer.RemoveChild(prevFrame);
        }

    }
}
