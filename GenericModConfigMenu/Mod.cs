using System.Collections.Generic;
using System.Linq;
using GenericModConfigMenu.Framework;
using GenericModConfigMenu.Integrations;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using SpaceShared;
using SpaceShared.UI;

using StardewModdingAPI;
using StardewModdingAPI.Events;

using StardewValley;
using StardewValley.Delegates;
using StardewValley.Menus;
using StardewValley.TokenizableStrings;
using StardewValley.Triggers;


namespace GenericModConfigMenu
{

    internal class Mod : StardewModdingAPI.Mod
    {
        public static Mod instance;

        /*********
        ** Fields
        *********/
        private OwnModConfig Config;
        private RootElement? Ui;
        private Button ConfigButton;

        private int countdown = 5;

        /// <summary>Manages registered mod config menus.</summary>
        internal readonly ModConfigManager ConfigManager = new();

        internal IStardewAccessApi StardewAccessApi;

        /*********
        ** Accessors
        *********/
        /// <summary>The current configuration menu.</summary>
        public static IClickableMenu ActiveConfigMenu
        {
            get
            {
                if (Game1.activeClickableMenu is TitleMenu)
                    return TitleMenu.subMenu;
                IClickableMenu menu = Game1.activeClickableMenu;
                if (menu == null) return null;
                while (menu.GetChildMenu() != null)
                    menu = menu.GetChildMenu();
                return menu is ModConfigMenu or SpecificModConfigMenu
                    ? menu
                    : null;
            }
            set
            {
                if (Game1.activeClickableMenu is TitleMenu)
                {
                    TitleMenu.subMenu = value;
                }
                else if (Game1.activeClickableMenu != null)
                {
                    var menu = Game1.activeClickableMenu;
                    while (menu.GetChildMenu() != null)
                        menu = menu.GetChildMenu();
                    if (value == null)
                    {
                        if (menu.GetParentMenu() != null)
                            menu.GetParentMenu().SetChildMenu(null);
                        else
                            Game1.activeClickableMenu = null;
                    }
                    else
                        menu.SetChildMenu(value);
                }
                else
                    Game1.activeClickableMenu = value;
            }
        }


        /*********
        ** Public methods
        *********/
        /// <inheritdoc />
        public override void Entry(IModHelper helper)
        {
            instance = this;
            I18n.Init(helper.Translation);
            Log.Monitor = this.Monitor;
            this.Config = helper.ReadConfig<OwnModConfig>();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicking += this.OnUpdateTicking;
            helper.Events.Display.WindowResized += this.OnWindowResized;
            helper.Events.Display.Rendered += this.OnRendered;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Input.MouseWheelScrolled += this.OnMouseWheelScrolled;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Input.ButtonsChanged += this.OnButtonChanged;

            helper.Events.Content.AssetRequested += static (_, e) => AssetManager.Apply(e);

            TriggerActionManager.RegisterAction("spacechase0.GenericModConfigMenu_OpenModConfig", (string[] args, TriggerActionContext ctx, out string error) =>
            {
                if (args.Length < 2)
                {
                    error = "Not enough arguments";
                    return false;
                }
                else if (!Helper.ModRegistry.IsLoaded(args[1]))
                {
                    error = $"Mod {args[1]} not loaded.";
                    return false;
                }
                var manifest = Helper.ModRegistry.Get(args[1]).Manifest;
                if (ConfigManager.Get(manifest, false) == null)
                {
                    error = $"Mod {args[1]} not registered with GMCM.";
                    return false;
                }

                OpenModMenuNew(manifest, null, null);

                error = null;
                return true;
            });
        }

        /// <inheritdoc />
        public override object GetApi(IModInfo mod)
        {
            return new Api(mod.Manifest, this.ConfigManager, mod => this.OpenModMenu(mod, page: null, listScrollRow: null), mod => this.OpenModMenuNew(mod, page: null, listScrollRow: null), (s) => LogDeprecated( mod.Manifest.UniqueID, s));
        }


        /*********
        ** Private methods
        *********/
        private static HashSet<string> DidDeprecationWarningsFor = new();
        private void LogDeprecated(string modid, string str)
        {
            if (DidDeprecationWarningsFor.Contains(modid))
                return;
            DidDeprecationWarningsFor.Add(modid);
            Log.Info(str);
        }

        /// <summary>Open the menu which shows a list of configurable mods.</summary>
        /// <param name="scrollRow">The initial scroll position, represented by the row index at the top of the visible area.</param>
        private void OpenListMenuNew(int? scrollRow = null)
        {
            Mod.ActiveConfigMenu = new ModConfigMenu(this.Config.ScrollSpeed, openModMenu: (mod, curScrollRow) => this.OpenModMenuNew(mod, page: null, listScrollRow: curScrollRow), openKeybindingsMenu: currScrollRow => OpenKeybindingsMenuNew( currScrollRow ), this.ConfigManager, this.Helper.GameContent.Load<Texture2D>(AssetManager.KeyboardButton), scrollRow);
        }
        private void OpenListMenu(int? scrollRow = null)
        {
            var newMenu = new ModConfigMenu(this.Config.ScrollSpeed, openModMenu: (mod, curScrollRow) => this.OpenModMenuNew(mod, page: null, listScrollRow: curScrollRow), openKeybindingsMenu: currScrollRow => OpenKeybindingsMenuNew(currScrollRow), this.ConfigManager, this.Helper.GameContent.Load<Texture2D>(AssetManager.KeyboardButton), scrollRow); ;
            if (Game1.activeClickableMenu is TitleMenu)
            {
                TitleMenu.subMenu = newMenu;
            }
            else
            {
                Game1.activeClickableMenu = newMenu;
            }
        }

        private void OpenKeybindingsMenuNew(int listScrollRow)
        {
            Mod.ActiveConfigMenu = new SpecificModConfigMenu(
                mods: this.ConfigManager,
                scrollSpeed: this.Config.ScrollSpeed,
                returnToList: () =>
                {
                    if (Game1.activeClickableMenu is TitleMenu)
                        OpenListMenuNew(listScrollRow);
                    else
                        Mod.ActiveConfigMenu = null;
                }
            );
        }

        private void OpenKeybindingsMenu(int listScrollRow)
        {
            var newMenu = new SpecificModConfigMenu(
                mods: this.ConfigManager,
                scrollSpeed: this.Config.ScrollSpeed,
                returnToList: () =>
                {
                    OpenListMenuNew(listScrollRow);
                }
            );

            if (Game1.activeClickableMenu is TitleMenu)
            {
                TitleMenu.subMenu = newMenu;
            }
            else
            {
                Game1.activeClickableMenu = newMenu;
            }
        }

        /// <summary>Open the config UI for a specific mod.</summary>
        /// <param name="mod">The mod whose config menu to display.</param>
        /// <param name="page">The page to display within the mod's config menu.</param>
        /// <param name="listScrollRow">The scroll position to set in the mod list when returning to it, represented by the row index at the top of the visible area.</param>
        private void OpenModMenuNew(IManifest mod, string page, int? listScrollRow)
        {
            ModConfig config = this.ConfigManager.Get(mod, assert: true);

            Mod.ActiveConfigMenu = new SpecificModConfigMenu(
                config: config,
                scrollSpeed: this.Config.ScrollSpeed,
                page: page,
                openPage: newPage =>
                {
                    if (!(Game1.activeClickableMenu is TitleMenu))
                        Mod.ActiveConfigMenu = null;
                    this.OpenModMenuNew(mod, newPage, listScrollRow);
                },
                returnToList: () =>
                {
                    if (Game1.activeClickableMenu is TitleMenu)
                        OpenListMenuNew(listScrollRow);
                    else
                        Mod.ActiveConfigMenu = null;
                }
            );
        }

        private void OpenModMenu(IManifest mod, string page, int? listScrollRow)
        {
            ModConfig config = this.ConfigManager.Get(mod, assert: true);

            var newMenu = new SpecificModConfigMenu(
                config: config,
                scrollSpeed: this.Config.ScrollSpeed,
                page: page,
                openPage: newPage =>
                {
                    OpenModMenuNew(mod, newPage, listScrollRow);
                },
                returnToList: () =>
                {
                    OpenListMenuNew(listScrollRow);
                }
            );

            if (Game1.activeClickableMenu is TitleMenu)
                TitleMenu.subMenu = newMenu;
            else
                Game1.activeClickableMenu = newMenu;
        }

        private void SetupTitleMenuButton()
        {
            if (this.Ui == null)
            {
                this.Ui = new RootElement();

                Texture2D tex = this.Helper.GameContent.Load<Texture2D>(AssetManager.ConfigButton);
                this.ConfigButton = new Button(tex)
                {
                    LocalPosition = new Vector2(36, Game1.viewport.Height - 100),
                    Callback = _ =>
                    {
                        Game1.playSound("newArtifact");
                        this.OpenListMenuNew();
                    },
                    ScreenReaderText = "GMCM"
                };

                this.Ui.AddChild(this.ConfigButton);
            }

            if (Game1.activeClickableMenu is TitleMenu tm && tm.allClickableComponents?.Find( (cc) => cc?.myID == 509800 ) == null )
            {
                // Gamepad support
                Texture2D tex = this.Helper.GameContent.Load<Texture2D>(AssetManager.ConfigButton);
                ClickableComponent button = new(new(0, Game1.viewport.Height - 100, tex.Width / 2, tex.Height / 2), "GMCM") // Why /2? Who knows
                {
                    myID = 509800,
                    rightNeighborID = tm.buttons[0].myID,
                };
                tm.allClickableComponents?.Add(button);
                tm.buttons[0].leftNeighborID = 509800;
            }
        }

        private bool IsTitleMenuInteractable()
        {
            if (Game1.activeClickableMenu is not TitleMenu titleMenu || TitleMenu.subMenu != null)
                return false;

            var method = this.Helper.Reflection.GetMethod(titleMenu, "ShouldAllowInteraction", false);
            if (method != null)
                return method.Invoke<bool>();
            else // method isn't available on Android
                return this.Helper.Reflection.GetField<bool>(titleMenu, "titleInPosition").GetValue();
        }

        /// <inheritdoc cref="IGameLoopEvents.GameLaunched"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // delay for long enough that CP can get a chance to edit
            // the texture.
            this.Helper.Events.GameLoop.UpdateTicking += this.FiveTicksAfterGameLaunched;

            Api configMenu = new Api(ModManifest, this.ConfigManager, mod => this.OpenModMenu(mod, page: null, listScrollRow: null), mod => this.OpenModMenuNew(mod, page: null, listScrollRow: null), (s) => LogDeprecated( ModManifest.UniqueID, s));

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new OwnModConfig(),
                save: () => this.Helper.WriteConfig(this.Config),
                titleScreenOnly: false
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: I18n.Options_ScrollSpeed_Name,
                tooltip: I18n.Options_ScrollSpeed_Desc,
                getValue: () => this.Config.ScrollSpeed,
                setValue: value => this.Config.ScrollSpeed = value,
                min: 1,
                max: 500,
                formatValue: null
            );

            configMenu.AddKeybindList(
                mod: this.ModManifest,
                name: I18n.Options_OpenMenuKey_Name,
                tooltip: I18n.Options_OpenMenuKey_Desc,
                getValue: () => this.Config.OpenMenuKey,
                setValue: value => this.Config.OpenMenuKey = value
            );

            // Initialize Stardew Access' Api
            this.StardewAccessApi = this.Helper.ModRegistry.GetApi<IStardewAccessApi>("shoaib.stardewaccess");
            if (this.StardewAccessApi is not null)
            {
                Log.Info("Initialized Stardew Access' api successfully");
                this.StardewAccessApi.RegisterCustomMenuAsAccessible(typeof(ModConfigMenu).FullName);
                this.StardewAccessApi.RegisterCustomMenuAsAccessible(typeof(SpecificModConfigMenu).FullName);

                Element.MouseHovered += (sender, args) =>
                {
                    Element element = ((Element)sender);
                    if (element.ScreenReaderIgnore) return;

                    this.StardewAccessApi.SayWithMenuChecker(this.GetScreenReaderInfoOfElement(element), true);
                };
            }
        }

        private string GetScreenReaderInfoOfElement(Element element)
        {
            string translationKey;
            string label = element.ScreenReaderText;
            object? tokens = new { label };

            switch (element)
            {
                case Button:
                    translationKey = "options_element-button_info";
                    break;
                case Checkbox checkbox:
                    translationKey = "options_element-checkbox_info";
                    tokens = new
                    {
                        label,
                        is_checked = checkbox.Checked ? 1 : 0
                    };
                    break;
                case Dropdown dropdown:
                    translationKey = "options_element-dropdown_info";
                    tokens = new
                    {
                        label,
                        selected_option = dropdown.Value
                    };
                    break;
                case Slider<float> slider:
                    translationKey = "options_element-slider_info";
                    tokens = new
                    {
                        label,
                        slider_value = slider.Value
                    };
                    break;
                case Slider<int> slider:
                    translationKey = "options_element-slider_info";
                    tokens = new
                    {
                        label,
                        slider_value = slider.Value
                    };
                    break;
                case Slider slider:
                    translationKey = "options_element-slider_info";
                    tokens = new
                    {
                        label,
                        slider_value = ((Slider<float>)slider).Value
                    };
                    break;
                case Textbox textbox:
                    translationKey = "options_element-text_box_info";
                    tokens = new
                    {
                        label,
                        value = string.IsNullOrEmpty(textbox.String) ? "null" : textbox.String,
                    };
                    break;
                case Label labelElement when label != null && label.EndsWith("[[InputListener]]"):
                    translationKey = "options_element-input_listener_info";
                    tokens = new
                    {
                        label = label.Replace("[[InputListener]]", ""),
                        buttons_list = labelElement.String
                    };
                    break;
                default:
                    return label;
            }

            if (string.IsNullOrWhiteSpace(label)) return "unknown";

            return $"{this.StardewAccessApi.Translate(translationKey, tokens, "Menu")}\n{element.ScreenReaderDescription}";
        }

        private void FiveTicksAfterGameLaunched(object sender, UpdateTickingEventArgs e)
        {
            if (this.countdown-- < 0)
            {
                this.SetupTitleMenuButton();
                this.Helper.Events.GameLoop.UpdateTicking -= this.FiveTicksAfterGameLaunched;
            }
        }

        private bool wasConfigMenu = false;

        /// <inheritdoc cref="IGameLoopEvents.UpdateTicking"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnUpdateTicking(object sender, UpdateTickingEventArgs e)
        {
            if (this.IsTitleMenuInteractable())
            {
                SetupTitleMenuButton();
                this.Ui?.Update();
            }

            if (wasConfigMenu && TitleMenu.subMenu == null)
            {
                var f = Helper.Reflection.GetField<bool>(Game1.activeClickableMenu, "titleInPosition");
                if (!f.GetValue())
                    f.SetValue(true);
            }
            wasConfigMenu = TitleMenu.subMenu is ModConfigMenu || TitleMenu.subMenu is SpecificModConfigMenu;
        }

        /// <inheritdoc cref="IDisplayEvents.WindowResized"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnWindowResized(object sender, WindowResizedEventArgs e)
        {
            if ( this.ConfigButton != null )
                this.ConfigButton.LocalPosition = new Vector2(this.ConfigButton.Position.X, Game1.viewport.Height - 100);
        }

        /// <inheritdoc cref="IDisplayEvents.Rendered"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnRendered(object sender, RenderedEventArgs e)
        {
            if (this.IsTitleMenuInteractable())
                this.Ui?.Draw(e.SpriteBatch);
        }

        /// <inheritdoc cref="IDisplayEvents.MenuChanged"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is GameMenu menu)
            {
                OptionsPage page = (OptionsPage)menu.pages[GameMenu.optionsTab];
                page.options.Add(new OptionsButton(I18n.Button_ModOptions(), () => this.OpenListMenuNew()));
            }
        }

        /// <inheritdoc cref="IInputEvents.ButtonPressed"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // open menu
            if (Context.IsPlayerFree && this.Config.OpenMenuKey.JustPressed())
                this.OpenListMenuNew();

            // pass input to menu
            else if (Mod.ActiveConfigMenu is SpecificModConfigMenu menu && e.Button.TryGetKeyboard(out Keys key))
                menu.receiveKeyPress(key);
        }

        /// <inheritdoc cref="IInputEvents.ButtonPressed"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnButtonChanged(object sender, ButtonsChangedEventArgs e)
        {
            // pass to menu for keybinding
            if (Mod.ActiveConfigMenu is SpecificModConfigMenu menu)
                menu.OnButtonsChanged(e);
        }

        /// <inheritdoc cref="IInputEvents.MouseWheelScrolled"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnMouseWheelScrolled(object sender, MouseWheelScrolledEventArgs e)
        {
            Dropdown.ActiveDropdown?.ReceiveScrollWheelAction(e.Delta);
        }
    }
}
