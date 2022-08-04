using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Monument Crates", "youtube/naumenkoff", "1.0.0")]
    internal class MonumentCrates : RustPlugin
    {
        private const string CratesPath = "MC/Crates";
        private static bool _pluginEnabled = true;
        private static List<LootCrates> _knownCrates = new List<LootCrates>();
        private static bool _isTimerEnabled;
        private readonly List<BasePlayer> _admins = new List<BasePlayer>();
        private readonly DynamicConfigFile _jsonCrates = Interface.Oxide.DataFileSystem.GetFile(CratesPath);
        private bool _registerCrates = true;
        private Timer _timer;

        private static int GetCrateCount(string name)
        {
            return _knownCrates?.Count(x => x.ShortPrefabName == name) ?? 0;
        }

        private static int KillCrates(IEnumerable<LootContainer> cratesCollection, bool whitelisted)
        {
            if (whitelisted)
                cratesCollection =
                    cratesCollection.Where(x => _config.WhitelistedContainers.Contains(x.ShortPrefabName));
            var destroyedContainers = 0;
            foreach (var lootContainer in cratesCollection)
            {
                destroyedContainers++;
                lootContainer.Kill();
                GameManager.Destroy(lootContainer, 0.1f);
            }

            return destroyedContainers;
        }

        private static void FindCrates(BasePlayer player, bool whitelisted)
        {
            var lootContainers = UnityEngine.Object.FindObjectsOfType<LootContainer>();
            if (whitelisted)
                lootContainers = lootContainers.Where(x => _config.WhitelistedContainers.Contains(x.ShortPrefabName))
                    .ToArray();
            List<string> containers = new List<string>(), buttonContainers = new List<string>();

            foreach (var container in lootContainers)
                if (containers.Contains(container.ShortPrefabName) == false &&
                    container.ShortPrefabName.Contains("roadsign") == false)
                    containers.Add(container.ShortPrefabName);

            containers.Sort();
            UserInterface.PreviousButtonsCollection = new List<CuiButton>();
            var buttons = UserInterface.GetButtons(containers);
            UserInterface.DrawLootContainersTypes(player, buttons);
        }

        // Event "Нажали на кнопку в списке контейнеров"
        private void ButtonDestroyCratesEvent(ConsoleSystem.Arg arg)
        {
            var text = arg.Args[2];
            if (string.IsNullOrEmpty(text)) return;
            var containers = UnityEngine.Object.FindObjectsOfType<LootContainer>()
                .Where(x => x.ShortPrefabName == text);
            var killedCrates = KillCrates(containers, false);
            var killedButton = UserInterface.PreviousButtonsCollection.Where(x => x.Text.Text == text).Select(x => x)
                .FirstOrDefault();
            if (UserInterface.PreviousButtonsCollection.Contains(killedButton))
                UserInterface.PreviousButtonsCollection.Remove(killedButton);
            UserInterface.DrawLootContainersTypes(arg.Player(), UserInterface.PreviousButtonsCollection);
            UserInterface.DrawOutput(arg.Player(), $"Удалено {killedCrates} {text}.");
        }

        // Event "Уничтожить все контейнеры" || "Уничтожить контейнеры из конфига на карте"
        private void UserDestroyCratesEvent(ConsoleSystem.Arg arg)
        {
            var whitelisted = arg.Args[1] == "/wl";
            var containers = UnityEngine.Object.FindObjectsOfType<LootContainer>();
            var destroyedContainers = KillCrates(containers, whitelisted);
            new PluginTimers(this).Once(5, () => FindCrates(arg.Player(), false));
            UserInterface.DrawOutput(arg.Player(), $"Уничтожено {destroyedContainers} контейнеров.");
        }

        // Event "Загрузить JSON"
        private static void UploadCrate(ConsoleSystem.Arg arg)
        {
            var oldList = _knownCrates;
            var newCrates = 0;
            foreach (var crate in oldList.Where(crate => _knownCrates.All(x =>
                         x.ShortPrefabName == crate.ShortPrefabName && x.ServerPosition == crate.ServerPosition)))
            {
                _knownCrates.Add(crate);
                newCrates++;
            }

            UserInterface.DrawOutput(arg.Player(), $"Добавлено {newCrates} контейнеров.");
        }

        private void TimerDestroyingCrates(ConsoleSystem.Arg arg)
        {
            _isTimerEnabled = !_isTimerEnabled;
            if (_isTimerEnabled)
                _timer = timer.Every(30, () =>
                {
                    var destroyedContainers = KillCrates(UnityEngine.Object.FindObjectsOfType<LootContainer>(), false);
                    UserInterface.DrawOutput(arg.Player(), $"Уничтожено {destroyedContainers} контейнеров.");
                });
            else timer.Destroy(ref _timer);
        }

        private void SpawnCrates(ConsoleSystem.Arg arg)
        {
            _registerCrates = false;
            foreach (var chest in (
                         from crate in _knownCrates.ToList()
                         select GameManager.server.CreateEntity(crate.LootContainer.PrefabName, crate.ServerPosition,
                             crate.ServerRotation)).OfType<LootContainer>()) chest.Spawn();
            _registerCrates = true;
            UserInterface.DrawOutput(arg.Player(), "Контейнеры из локального списка заспавнены!");
        }

        private static class CommandsList
        {
            public const string Plugin = "mc.plugin";
            public const string Json = "mc.json";
            public const string Containers = "mc.containers";
            public const string Menu = "mc.menu";
        }

        private static class UserInterface
        {
            private static bool _isMenuActive;
            private static readonly List<string> Messages = new List<string>();
            public static List<CuiButton> PreviousButtonsCollection = new List<CuiButton>();

            public static List<string> PreviousNamesCollection = new List<string>();

            private static string GetOpenCommand(string link, string section)
            {
                return link + ' ' + section;
            }

            public static void ClearMessages(BasePlayer player)
            {
                Messages.Clear();
                DrawOutput(player, null);
            }

            public static void OpenCratesManager(BasePlayer player, bool isMenuActive)
            {
                Destroy(player);
                _isMenuActive = isMenuActive;
                var menu = new CuiElementContainer
                {
                    {
                        new CuiButton
                        {
                            RectTransform =
                            {
                                AnchorMin = "0.9 0.96", AnchorMax = "1 1"
                            },
                            Button =
                            {
                                Sprite = "assets/content/textures/generic/fulltransparent.tga",
                                Color = "0 0 0 0",
                                Command = BuildFormat(CommandsList.Menu, isMenuActive ? "close" : "open")
                            },
                            Text =
                            {
                                Text = isMenuActive ? "Close" : "Open Crates Manager", Align = TextAnchor.MiddleCenter,
                                FontSize = 12, Color = GetColor("#edededff")
                            }
                        },
                        "Overlay", UILayers.OpenManagerButton
                    }
                };
                CuiHelper.AddUi(player, menu);
            }

            public static void DrawCratesManager(BasePlayer player, bool isActive)
            {
                Destroy(player);
                OpenCratesManager(player, isActive);
                _isMenuActive = isActive;
                if (!isActive) return;
                DrawPluginSection(player);
                DrawJsonSection(player);
                DrawManagerSection(player);
                DrawLootContainersTypes(player, PreviousButtonsCollection);
                DrawOutput(player, string.Empty);
                DrawCratesCount(player);
            }

            public static void DrawPluginSection(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UILayers.PluginSection);
                if (!_isMenuActive) return;
                var cui = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                Color = GetColor("00000075")
                            },
                            RectTransform = {AnchorMin = "0.04 0.25", AnchorMax = "0.24 0.75"},
                            CursorEnabled = true
                        },
                        "Overlay", UILayers.PluginSection
                    },
                    new CuiElement
                    {
                        Parent = UILayers.PluginSection,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Plugin"
                            },
                            new CuiRectTransformComponent {AnchorMin = "0 0.8", AnchorMax = "1 1"}
                        }
                    },

                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(PluginSector.Plugin, PluginSector.State)
                            },
                            RectTransform = {AnchorMin = "0.1 0.65", AnchorMax = "0.9 0.8"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = _pluginEnabled ? "Выключить плагин" : "Включить плагин"
                            }
                        },
                        UILayers.PluginSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(PluginSector.Plugin, PluginSector.Notifications,
                                    PluginSector.Show)
                            },
                            RectTransform = {AnchorMin = "0.1 0.25", AnchorMax = "0.9 0.4"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = _config.Notifications
                                    ? "Показывать только системные уведомления"
                                    : "Показывать все уведомления"
                            }
                        },
                        UILayers.PluginSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(PluginSector.Plugin, PluginSector.Notifications,
                                    PluginSector.Clear)
                            },
                            RectTransform = {AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.2"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Очистить консоль"
                            }
                        },
                        UILayers.PluginSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(PluginSector.Plugin, PluginSector.Timer)
                            },
                            RectTransform = {AnchorMin = "0.1 0.45", AnchorMax = "0.9 0.6"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = _isTimerEnabled ? "Выключить таймер" : "Включить таймер"
                            }
                        },
                        UILayers.PluginSection
                    }
                };
                CuiHelper.AddUi(player, cui);
            }

            public static void DrawJsonSection(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UILayers.JsonSection);
                if (!_isMenuActive) return;

                var cui = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                Color = GetColor("00000075")
                            },
                            RectTransform = {AnchorMin = "0.28 0.25", AnchorMax = "0.48 0.75"},
                            CursorEnabled = true
                        },
                        "Overlay", UILayers.JsonSection
                    },
                    new CuiElement
                    {
                        Parent = UILayers.JsonSection,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Json"
                            },
                            new CuiRectTransformComponent {AnchorMin = "0 0.8", AnchorMax = "1 1"}
                        }
                    }
                };

                cui.Add(new CuiButton
                {
                    Button =
                    {
                        Color = "0.25 0.25 0.25 0.5",
                        Command = BuildFormat(JsonSector.Json, JsonSector.Autosave)
                    },
                    RectTransform = {AnchorMin = "0.1 0.68", AnchorMax = "0.9 0.8"},
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = _config.Autosave ? "Выключить автосохранение" : "Включить автосохранение"
                    }
                }, UILayers.JsonSection);

                cui.Add(new CuiButton
                {
                    Button =
                    {
                        Color = "0.25 0.25 0.25 0.5",
                        Command = BuildFormat(JsonSector.Json, JsonSector.Save)
                    },
                    RectTransform = {AnchorMin = "0.1 0.20", AnchorMax = "0.9 0.32"},
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "Сохранить локальную Data в Json"
                    }
                }, UILayers.JsonSection);

                cui.Add(new CuiButton
                {
                    Button =
                    {
                        Color = "0.25 0.25 0.25 0.5",
                        Command = BuildFormat(JsonSector.Json, JsonSector.Clear, JsonSector.Physical)
                    },
                    RectTransform = {AnchorMin = "0.1 0.52", AnchorMax = "0.9 0.64"},
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "Очистить физический список контейнеров"
                    }
                }, UILayers.JsonSection);

                cui.Add(new CuiButton
                {
                    Button =
                    {
                        Color = "0.25 0.25 0.25 0.5",
                        Command = BuildFormat(JsonSector.Json, JsonSector.Clear, JsonSector.Local)
                    },
                    RectTransform = {AnchorMin = "0.1 0.36", AnchorMax = "0.9 0.48"},
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "Очистить локальный список контейнеров"
                    }
                }, UILayers.JsonSection);

                cui.Add(new CuiButton
                {
                    Button =
                    {
                        Color = "0.25 0.25 0.25 0.5",
                        Command = BuildFormat(JsonSector.Json, JsonSector.Upload)
                    },
                    RectTransform = {AnchorMin = "0.1 0.04", AnchorMax = "0.9 0.16"},
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                        Text = "Загрузить Json"
                    }
                }, UILayers.JsonSection);

                CuiHelper.AddUi(player, cui);
            }

            public static void DrawManagerSection(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UILayers.ManagerSection);
                if (!_isMenuActive) return;
                var cui = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                Color = GetColor("00000075")
                            },
                            RectTransform = {AnchorMin = "0.76 0.25", AnchorMax = "0.96 0.75"},
                            CursorEnabled = true
                        },
                        "Overlay", UILayers.ManagerSection
                    },
                    new CuiElement
                    {
                        Parent = UILayers.ManagerSection,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "LootContainer Manager"
                            },
                            new CuiRectTransformComponent {AnchorMin = "0 0.8", AnchorMax = "1 1"}
                        }
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(ContainersSector.Containers, ContainersSector.Find,
                                    ContainersSector.Whitelisted)
                            },
                            RectTransform = {AnchorMin = "0.1 0.04", AnchorMax = "0.9 0.16"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Найти контейнеры из конфига"
                            }
                        },
                        UILayers.ManagerSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(ContainersSector.Containers, ContainersSector.Destroy,
                                    ContainersSector.Whitelisted)
                            },

                            RectTransform = {AnchorMin = "0.1 0.36", AnchorMax = "0.9 0.48"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Уничтожить контейнеры из конфига на карте"
                            }
                        },
                        UILayers.ManagerSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(ContainersSector.Containers, ContainersSector.Find,
                                    ContainersSector.Find, ContainersSector.All)
                            },
                            RectTransform = {AnchorMin = "0.1 0.2", AnchorMax = "0.9 0.32"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Найти все контейнеры на карте"
                            }
                        },
                        UILayers.ManagerSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(ContainersSector.Containers, ContainersSector.Destroy,
                                    ContainersSector.All)
                            },
                            RectTransform = {AnchorMin = "0.1 0.52", AnchorMax = "0.9 0.64"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Уничтожить все контейнеры"
                            }
                        },
                        UILayers.ManagerSection
                    },
                    {
                        new CuiButton
                        {
                            Button =
                            {
                                Color = "0.25 0.25 0.25 0.5",
                                Command = BuildFormat(ContainersSector.Containers, ContainersSector.Spawn)
                            },
                            RectTransform = {AnchorMin = "0.1 0.68", AnchorMax = "0.9 0.8"},
                            Text =
                            {
                                Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 15,
                                Text = "Заспавнить контейнеры"
                            }
                        },
                        UILayers.ManagerSection
                    }
                };
                CuiHelper.AddUi(player, cui);
            }

            public static void DrawLootContainersTypes(BasePlayer player, List<CuiButton> buttons)
            {
                CuiHelper.DestroyUi(player, UILayers.LootContainersInformation);
                if (!_isMenuActive) return;

                var hud = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                Color = GetColor("00000075")
                            },
                            RectTransform = {AnchorMin = "0.52 0.25", AnchorMax = "0.72 0.75"},
                            CursorEnabled = true
                        },
                        "Overlay", UILayers.LootContainersInformation
                    }
                };
                if (buttons.IsNullOrEmpty() == false)
                {
                    foreach (var button in buttons) hud.Add(button, UILayers.LootContainersInformation);

                    PreviousButtonsCollection = buttons;
                }

                CuiHelper.AddUi(player, hud);
            }

            public static List<CuiButton> GetButtons(IReadOnlyList<string> names)
            {
                var buttons = new List<CuiButton>();
                if (names.Count == 0)
                {
                    PreviousNamesCollection = new List<string>();
                    return null;
                }

                PreviousNamesCollection = names.ToList();
                const float startX = 0.025f;
                const float startY = 0.02f;
                const float buttonWidth = 0.3f;
                const float buttonHeight = 0.08f;

                var currentElement = 0;
                var columnsCount = names.Count < 3 ? 1 : names.Count / 3 + names.Count % 3;
                for (var columns = 0; columns < columnsCount; columns++)
                for (var item = 0; item < 3; item++)
                {
                    if (currentElement == names.Count) return buttons;
                    var currentX = startX + (startX * item + buttonWidth * item);
                    var currentY = startY + (startY * columns + buttonHeight * columns);

                    var button = new CuiButton
                    {
                        Button =
                        {
                            Color = "0.25 0.25 0.25 0.5",
                            Command = CommandsList.Containers + ' ' +
                                      $"destroy /s {(currentElement < names.Count ? names[currentElement] : string.Empty)}"
                        },
                        Text =
                        {
                            Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", FontSize = 8,
                            Text = currentElement < names.Count ? names[currentElement] : "null!"
                        },
                        RectTransform =
                        {
                            AnchorMin = $"{currentX} {currentY}",
                            AnchorMax = $"{currentX + buttonWidth} {currentY + buttonHeight}"
                        }
                    };
                    buttons.Add(button);
                    currentElement++;
                }

                // 0.07 -> 0.31, 0.38 -> 0.62, 0.69 -> 0.93
                return buttons;
            }

            public static void DrawCratesCount(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, UILayers.ContainersInformation);
                if (!_isMenuActive) return;
                var hud = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                Color = GetColor("00000075")
                            },
                            RectTransform = {AnchorMin = "0.52 0.16", AnchorMax = "0.96 0.24"},
                            CursorEnabled = true
                        },
                        "Overlay", UILayers.ContainersInformation
                    },
                    {
                        new CuiLabel
                        {
                            Text =
                            {
                                Text = $"<color=lime>Military</color> {GetCrateCount("crate_normal")} | " +
                                       $"Crate {GetCrateCount("crate_normal_2")} | " +
                                       $"<color=yellow>Primitive</color> {GetCrateCount("crate_basic")} | " +
                                       $"Ration {GetCrateCount("foodbox")} | " +
                                       $"<color=cyan>Vehicle</color> {GetCrateCount("vehicle_parts")} | " +
                                       $"Elite {GetCrateCount("crate_elite")}",
                                Align = TextAnchor.MiddleCenter, FontSize = 12,
                                Color = "1 1 1 1"
                            },
                            RectTransform = {AnchorMin = "0 0", AnchorMax = "1 1"}
                        },
                        UILayers.ContainersInformation
                    }
                };
                CuiHelper.AddUi(player, hud);
            }

            public static void DrawOutput(BasePlayer player, string text)
            {
                CuiHelper.DestroyUi(player, UILayers.PluginConsole);
                if (!string.IsNullOrEmpty(text)) Messages.Add(text);
                if (!_isMenuActive) return;
                if (Messages.Count >= 30) Messages.RemoveRange(0, Messages.Count - 15);
                var hud = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                                Color = GetColor("00000075")
                            },
                            RectTransform = {AnchorMin = "0.04 0.16", AnchorMax = "0.48 0.24"},
                            CursorEnabled = true
                        },
                        "Overlay", UILayers.PluginConsole
                    },
                    {
                        new CuiLabel
                        {
                            Text =
                            {
                                Text = $"{GetMessage(Messages.Count)}\n" +
                                       $"{GetMessage(Messages.Count - 1)}\n" +
                                       $"{GetMessage(Messages.Count - 2)}\n" +
                                       $"{GetMessage(Messages.Count - 3)}\n" +
                                       $"{GetMessage(Messages.Count - 4)}",
                                Align = TextAnchor.MiddleCenter, FontSize = 8,
                                Color = "1 1 1 1"
                            },
                            RectTransform = {AnchorMin = "0.02 0.04", AnchorMax = "0.327 0.96"}
                        },
                        UILayers.PluginConsole
                    },
                    // 0.307
                    {
                        new CuiLabel
                        {
                            Text =
                            {
                                Text = $"{GetMessage(Messages.Count - 5)}\n" +
                                       $"{GetMessage(Messages.Count - 6)}\n" +
                                       $"{GetMessage(Messages.Count - 7)}\n" +
                                       $"{GetMessage(Messages.Count - 8)}\n" +
                                       $"{GetMessage(Messages.Count - 9)}",
                                Align = TextAnchor.MiddleCenter, FontSize = 8,
                                Color = "1 1 1 1"
                            },
                            RectTransform = {AnchorMin = "0.347 0.04", AnchorMax = "0.654 0.96"}
                        },
                        UILayers.PluginConsole
                    },
                    {
                        new CuiLabel
                        {
                            Text =
                            {
                                Text = $"{GetMessage(Messages.Count - 10)}\n" +
                                       $"{GetMessage(Messages.Count - 11)}\n" +
                                       $"{GetMessage(Messages.Count - 12)}\n" +
                                       $"{GetMessage(Messages.Count - 13)}\n" +
                                       $"{GetMessage(Messages.Count - 14)}",
                                Align = TextAnchor.MiddleCenter, FontSize = 8,
                                Color = "1 1 1 1"
                            },
                            RectTransform = {AnchorMin = "0.674 0.04", AnchorMax = "0.98 0.96"}
                        },
                        UILayers.PluginConsole
                    }
                };
                CuiHelper.AddUi(player, hud);
            }

            public static void Destroy(BasePlayer player)
            {
                foreach (var layer in UILayers.Layers) CuiHelper.DestroyUi(player, layer);
            }

            private static string GetMessage(int index)
            {
                return index > 0 && Messages.Count >= index ? Messages[index - 1] : string.Empty;
            }

            private static byte ParseString(string str, int startIndex, int length)
            {
                return byte.Parse(str.Substring(startIndex, length), NumberStyles.HexNumber);
            }

            private static string GetColor(string hex)
            {
                if (string.IsNullOrEmpty(hex)) hex = "#FFFFFFFF";
                var str = hex.Trim('#');
                if (str.Length == 6) str += "FF";
                Color color = new Color32(ParseString(str, 0, 2), ParseString(str, 2, 2), ParseString(str, 4, 2),
                    ParseString(str, 6, 2));
                return $"{color.r:F2} {color.g:F2} {color.b:F2} {color.a:F2}";
            }

            private static class UILayers
            {
                public const string ContainersInformation = "ContainersInformation";
                public const string PluginConsole = "PluginConsole";
                public const string JsonSection = "JsonSection";
                public const string PluginSection = "PluginSection";
                public const string OpenManagerButton = "OpenContainersManager";
                public const string ManagerSection = "ManagerSection";
                public const string LootContainersInformation = "LootContainersInformation";

                public static readonly List<string> Layers = new List<string>
                {
                    ContainersInformation, PluginConsole, JsonSection, PluginSection, OpenManagerButton, ManagerSection,
                    LootContainersInformation
                };
            }
        }

        #region Config & Data

        private void SaveCrates()
        {
            _jsonCrates.Settings = new JsonSerializerSettings {ReferenceLoopHandling = ReferenceLoopHandling.Ignore};
            _jsonCrates.WriteObject(_knownCrates);
        }

        private void ReadCrate()
        {
            _jsonCrates.Settings = new JsonSerializerSettings {ReferenceLoopHandling = ReferenceLoopHandling.Ignore};
            _knownCrates = Interface.Oxide.DataFileSystem.ReadObject<List<LootCrates>>(CratesPath) ??
                           new List<LootCrates>();
        }

        private class LootCrates
        {
            public LootCrates(LootContainer lootContainer)
            {
                LootContainer = lootContainer;
                ShortPrefabName = lootContainer.ShortPrefabName;
                ServerPosition = lootContainer.ServerPosition;
                ServerRotation = lootContainer.ServerRotation;
                PrefabName = lootContainer.PrefabName;
            }

            [JsonConstructor]
            public LootCrates(string prefabName, Vector3 serverPosition, Quaternion serverRotation)
            {
                PrefabName = prefabName;
                ServerPosition = serverPosition;
                ServerRotation = serverRotation;
                var lootContainer = GameManager.server.CreateEntity(prefabName, serverPosition, serverRotation);
                LootContainer = lootContainer as LootContainer;
                ShortPrefabName = lootContainer.ShortPrefabName;
            }

            [JsonIgnore] public LootContainer LootContainer { get; }
            [JsonProperty("Prefab Name")] private string PrefabName { get; }
            [JsonProperty("Name")] public string ShortPrefabName { get; }
            [JsonProperty("Position")] public Vector3 ServerPosition { get; }
            [JsonProperty("Rotation")] public Quaternion ServerRotation { get; }
        }


        private static Configuration _config;

        private class Configuration
        {
            [JsonProperty("Белый список контейнеров")]
            public List<string> WhitelistedContainers { get; set; }

            [JsonProperty("Автоматическое сохранение")]
            public bool Autosave { get; set; }

            [JsonProperty("Фильтр уведомлений: true - показываются все, false - только системные")]
            public bool Notifications { get; set; }

            public static Configuration LoadDefaultConfig()
            {
                return new Configuration
                {
                    WhitelistedContainers = new List<string>
                    {
                        "crate_normal", "crate_normal_2", "crate_basic", "foodbox", "vehicle_parts", "vehicle_parts",
                        "crate_elite"
                    },
                    Autosave = true,
                    Notifications = true
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        protected override void LoadDefaultConfig()
        {
            _config = Configuration.LoadDefaultConfig();
        }

        #endregion

        #region Oxide Events

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity.GetType() != typeof(LootContainer) || _pluginEnabled == false ||
                _registerCrates == false) return;
            if (_config.WhitelistedContainers.Contains(entity.ShortPrefabName) == false) return;

            var lootCrate = new LootCrates(entity as LootContainer);

            if (_knownCrates.Any(x =>
                    x.ServerPosition == lootCrate.ServerPosition &&
                    x.ShortPrefabName == lootCrate.ShortPrefabName)) return;
            _knownCrates.Add(lootCrate);
            if (UserInterface.PreviousNamesCollection.Any(x => x == lootCrate.ShortPrefabName) == false)
            {
                UserInterface.PreviousNamesCollection.Add(lootCrate.ShortPrefabName);
                var buttons = UserInterface.GetButtons(UserInterface.PreviousNamesCollection);
                foreach (var player in _admins) UserInterface.DrawLootContainersTypes(player, buttons);
            }

            if (!_config.Notifications) return;
            foreach (var player in _admins)
                UserInterface.DrawOutput(player,
                    $"{entity.ShortPrefabName}, <color=orange>{entity.transform.position}</color>");
        }

        private void OnServerSave()
        {
            if (_config.Autosave) SaveCrates();
        }

        private void Loaded()
        {
            ReadCrate();
            foreach (var player in BasePlayer.activePlayerList.Where(player => player.IsAdmin))
                OnPlayerConnected(player);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (!player.IsAdmin) return;
            UserInterface.OpenCratesManager(player, false);
            timer.Every(1.5f, () => { UserInterface.DrawCratesCount(player); });
            _admins.Add(player);
        }

        private void Unload()
        {
            if (_config.Autosave) SaveCrates();
            foreach (var player in BasePlayer.activePlayerList.Where(player => player.IsAdmin))
                UserInterface.Destroy(player);
        }

        #endregion

        #region Oxide Command Invokes

        [ConsoleCommand(CommandsList.Menu)]
        private void MenuState(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs()) return;
            UserInterface.DrawCratesManager(arg.Player(), arg.Args[0] == "open");
            if (UserInterface.PreviousButtonsCollection.IsNullOrEmpty()) FindCrates(arg.Player(), false);
        }

        [ConsoleCommand(CommandsList.Plugin)]
        private void Plugin(ConsoleSystem.Arg arg)
        {
            if (PluginSector.ChangePlugin(arg, _pluginEnabled)) _pluginEnabled = !_pluginEnabled;
            if (PluginSector.CanStartTimer(arg, _isTimerEnabled)) TimerDestroyingCrates(arg);

            if (PluginSector.CanDisableNotifications(arg, _config.Notifications))
            {
                _config.Notifications = !_config.Notifications;
                SaveConfig();
            }

            if (PluginSector.CanClearNotifications(arg)) UserInterface.ClearMessages(arg.Player());
            UserInterface.DrawPluginSection(arg.Player());
        }

        [ConsoleCommand(CommandsList.Json)]
        private void JsonManager(ConsoleSystem.Arg arg)
        {
            if (JsonSector.ChangeAutosave(arg, _config.Autosave))
            {
                _config.Autosave = !_config.Autosave;
                SaveConfig();
            }

            if (JsonSector.CanSaveCrates(arg)) SaveCrates();
            if (JsonSector.CanClearLocalData(arg)) _knownCrates.Clear();
            if (JsonSector.CanClearPhysicalData(arg))
            {
                _jsonCrates.Clear();
                _jsonCrates.Save();
            }

            if (JsonSector.CanUploadCrates(arg)) UploadCrate(arg);
            UserInterface.DrawJsonSection(arg.Player());
        }

        [ConsoleCommand(CommandsList.Containers)]
        private void ContainersManager(ConsoleSystem.Arg arg)
        {
            if (ContainersSector.CanSpawnCrates(arg)) SpawnCrates(arg);

            if (ContainersSector.CanFindCrates(arg))
            {
                UserInterface.DrawOutput(arg.Player(), "Начал искать контейнеры на карте");
                FindCrates(arg.Player(), arg.Args[1] == "/wl");
            }

            if (ContainersSector.CanDestroyCrates(arg))
            {
                if (ContainersSector.CanDestroyCratesSilence(arg)) ButtonDestroyCratesEvent(arg);
                else if (ContainersSector.CanDestroyWhitelistedCrates(arg)) UserDestroyCratesEvent(arg);
            }

            UserInterface.DrawManagerSection(arg.Player());
        }

        #endregion

        #region Command Logic

        private static class JsonSector
        {
            public const string Json = "mc.json";
            public const string Autosave = "autosave";
            public const string Save = "save";
            public const string Clear = "clear";
            public const string Local = "/l";
            public const string Upload = "upload";
            public const string Physical = "/p";

            public static bool ChangeAutosave(ConsoleSystem.Arg arg, bool autosave)
            {
                if (arg.Args[0] != Autosave) return false;
                UserInterface.DrawOutput(arg.Player(),
                    autosave
                        ? "Автоматическое сохранение контейнеров выключено"
                        : "Автоматическое сохранение контейнеров включено");
                return true;
            }

            public static bool CanSaveCrates(ConsoleSystem.Arg arg)
            {
                if (arg.Args[0] != Save) return false;
                UserInterface.DrawOutput(arg.Player(), "Локальный список контейнеров сохранен");
                return true;
            }

            public static bool CanClearLocalData(ConsoleSystem.Arg arg)
            {
                if (arg.Args[0] != Clear) return false;
                if (arg.Args[1] != Local) return false;
                UserInterface.DrawOutput(arg.Player(), "Локальный список контейнеров очищен");
                return true;
            }

            public static bool CanClearPhysicalData(ConsoleSystem.Arg arg)
            {
                if (arg.Args[0] != Clear) return false;
                if (arg.Args[1] != Physical) return false;
                UserInterface.DrawOutput(arg.Player(), "Физический список контейнеров очищен");
                return true;
            }

            public static bool CanUploadCrates(ConsoleSystem.Arg arg)
            {
                return arg.Args[0] == Upload;
            }
        }

        private static class PluginSector
        {
            public const string Plugin = "mc.plugin";
            public const string State = "state";
            public const string Timer = "timer";
            public const string Notifications = "notifications";
            public const string Clear = "clear";
            public const string Show = "show";

            public static bool ChangePlugin(ConsoleSystem.Arg arg, bool pluginEnabled)
            {
                if (arg.Args[0] != State) return false;
                UserInterface.DrawOutput(arg.Player(), pluginEnabled ? "Плаген выключен" : "Плагин включен");
                return true;
            }

            public static bool CanStartTimer(ConsoleSystem.Arg arg, bool timerEnabled)
            {
                if (arg.Args[0] != Timer) return false;
                UserInterface.DrawOutput(arg.Player(), timerEnabled ? "Таймер остановлен" : "Таймер запущен");
                return true;
            }

            public static bool CanClearNotifications(ConsoleSystem.Arg arg)
            {
                if (arg?.Args[0] != Notifications) return false;
                if (arg.Args[1] != Clear) return false;
                return true;
            }

            public static bool CanDisableNotifications(ConsoleSystem.Arg arg, bool notifications)
            {
                if (arg.Args[0] != Notifications) return false;
                if (arg.Args[1] != Show) return false;
                UserInterface.DrawOutput(arg.Player(),
                    notifications ? "Показываются только системные уведомления" : "Показываются все уведомления");
                return true;
            }
        }

        private static class ContainersSector
        {
            public const string Containers = "mc.containers";
            public const string Spawn = "spawn";
            public const string Destroy = "destroy";
            public const string Find = "find";
            public const string Whitelisted = "/wl";
            public const string Silence = "/s";
            public const string All = "/all";

            public static bool CanSpawnCrates(ConsoleSystem.Arg arg)
            {
                return arg.Args[0] == Spawn;
            }

            public static bool CanDestroyCrates(ConsoleSystem.Arg arg)
            {
                return arg.Args[0] == Destroy;
            }

            public static bool CanFindCrates(ConsoleSystem.Arg arg)
            {
                return arg.Args[0] == Find;
            }

            public static bool CanDestroyCratesSilence(ConsoleSystem.Arg arg)
            {
                return arg.Args[0] == Destroy && arg.Args[1] == Silence;
            }

            public static bool CanDestroyWhitelistedCrates(ConsoleSystem.Arg arg)
            {
                return arg.Args[0] == Destroy && arg.Args[1] != Silence;
            }
        }

        public static string BuildFormat(params string[] text)
        {
            var lmao = text.Aggregate(string.Empty, (current, word) => current + word + " ");
            return lmao.TrimEnd();
        }

        #endregion

        /*
         mc.plugin (добавить аргумент /s == state)
         mc.plugin notifications (добавить аргумент /s == state)
         mc.plugin notifications clear
         mc.plugin timer (добавить аргумент /s == state)
         mc.json autosave (добавить аргумент /s == state)
         mc.json save
         mc.json clear (добавить аргумент /a == all)
         mc.json clear /l
         mc.json upload
         mc.containers find /wl
         mc.containers destroy /wl
         mc.containers find /all
         mc.containers destroy /all
         mc.containers spawn
         */
    }
}