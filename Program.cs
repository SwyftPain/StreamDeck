using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using OpenMacroBoard.SDK;
using StreamDeckSharp;
using PluginContracts;

namespace StreamDeckMiniGuiExample
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private Button[] keyButtons;
        private ActionDetails[] keyActions;
        private TextBox outputTextBox;
        private IMacroBoard device;
        private Panel actionsPanel;
        private Panel configurationPanel;
        private Label actionConfigLabel;
        private List<IPluginAction> plugins = new List<IPluginAction>();
        private Dictionary<int, Control> keyConfigurations = new Dictionary<int, Control>();

        public MainForm()
        {
            Text = "Stream Deck Mini GUI";
            Size = new Size(1070, 500);
            keyButtons = new Button[6];
            keyActions = new ActionDetails[6];

            for (int i = 0; i < keyActions.Length; i++)
            {
                keyActions[i] = new ActionDetails();
            }

            InitializeGUI();
            InitializeStreamDeck();
            LoadPlugins();
        }

        private void InitializeGUI()
        {
            this.BackColor = Color.FromArgb(30, 30, 30);

            actionsPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
                BackColor = Color.FromArgb(40, 40, 40),
                AutoScroll = true
            };
            Controls.Add(actionsPanel);

            configurationPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 250,
                BackColor = Color.FromArgb(40, 40, 40)
            };
            Controls.Add(configurationPanel);

            actionConfigLabel = new Label
            {
                Text = "Action Configuration",
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 50, 50)
            };
            configurationPanel.Controls.Add(actionConfigLabel);

            for (int i = 0; i < 6; i++)
            {
                keyButtons[i] = new Button
                {
                    Text = $"Key {i + 1}",
                    Size = new Size(180, 80),
                    BackColor = Color.FromArgb(50, 50, 50),
                    Font = new Font("Segoe UI", 16, FontStyle.Bold),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.White,
                    Margin = new Padding(5)
                };

                int index = i;
                keyButtons[i].Click += (sender, e) => ConfigureAction(index);
                keyButtons[i].AllowDrop = true;
                keyButtons[i].DragEnter += (s, e) => e.Effect = DragDropEffects.Copy;
                keyButtons[i].DragDrop += (s, e) => AssignActionToKey(index, e.Data.GetData(typeof(ActionDetails)) as ActionDetails);

                int col = i % 3;
                int row = i / 3;
                keyButtons[i].Location = new Point(210 + (col * 200), 20 + (row * 100));
                Controls.Add(keyButtons[i]);
            }

            outputTextBox = new TextBox
            {
                Multiline = true,
                Location = new Point(210, 350),
                Size = new Size(580, 100),
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10),
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Controls.Add(outputTextBox);

            LoadAvailableActions();
        }

        private void LoadPlugins()
        {
            string pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            LogOutput($"Checking for plugins in: {pluginPath}");

            if (Directory.Exists(pluginPath))
            {
                var dllFiles = Directory.GetFiles(pluginPath, "*.dll", SearchOption.AllDirectories);
                LogOutput($"Found {dllFiles.Length} DLL(s) in plugin directory.");

                foreach (var dll in dllFiles)
                {
                    try
                    {
                        LogOutput($"Loading plugin from {dll}");
                        Assembly assembly = Assembly.LoadFrom(dll);
                        bool pluginFound = false;

                        foreach (var type in assembly.GetTypes())
                        {
                            if (typeof(IPluginAction).IsAssignableFrom(type) && !type.IsAbstract)
                            {
                                IPluginAction plugin = (IPluginAction)Activator.CreateInstance(type);
                                plugins.Add(plugin);
                                LogOutput($"Loaded plugin: {plugin.Name}");
                                pluginFound = true;
                            }
                        }

                        if (!pluginFound)
                        {
                            LogOutput($"No valid plugins found in {dll}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogOutput($"Failed to load plugin from {dll}: {ex.Message}");
                    }
                }
            }
            else
            {
                LogOutput($"Plugin directory does not exist: {pluginPath}");
            }

            LoadAvailableActions();
        }

        private void LoadAvailableActions()
        {
            var actions = new List<ActionDetails>
            {

            };

            foreach (var plugin in plugins)
            {
                var actionDetails = plugin.GetActionDetails();
                LogOutput($"Adding plugin action: {actionDetails.ActionName}");
                actions.Add(actionDetails);
            }

            int buttonHeight = 50;
            int verticalSpacing = 10;
            int currentY = 10;

            foreach (var action in actions)
            {
                var actionButton = new Button
                {
                    Text = action.ActionName,
                    Size = new Size(180, buttonHeight),
                    BackColor = Color.FromArgb(70, 70, 70),
                    ForeColor = Color.White,
                    AllowDrop = false
                };

                actionButton.Location = new Point(10, currentY);

                actionButton.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        DoDragDrop(action, DragDropEffects.Copy);
                    }
                };

                actionsPanel.Controls.Add(actionButton);
                currentY += buttonHeight + verticalSpacing;
            }
        }

        private void InitializeStreamDeck()
        {
            device = StreamDeck.OpenDevice();
            device.SetBrightness(100);

            for (int i = 0; i < 6; i++)
            {
                keyActions[i] = new ActionDetails();
                UpdateKeyAppearance(i);
            }

            device.KeyStateChanged += (sender, e) =>
            {
                if (e.IsDown)
                {
                    ExecuteAction(e.Key);
                }
            };
        }

        private void AssignActionToKey(int keyIndex, ActionDetails actionDetails)
        {
            keyActions[keyIndex] = new ActionDetails
            {
                ActionType = actionDetails.ActionType,
                MessageToPrint = actionDetails.MessageToPrint,
                CommandToRun = actionDetails.CommandToRun,
                ActionName = actionDetails.ActionName,
                ActionId = actionDetails.ActionId
            };

            var plugin = plugins.FirstOrDefault(p => p.ActionId == actionDetails.ActionId);
            if (plugin != null)
            {
                keyConfigurations[keyIndex] = plugin.GetConfigurationControl();
            }

            UpdateKeyAppearance(keyIndex);
            LogOutput($"Assigned {actionDetails.ActionName} to Key {keyIndex + 1}");
        }

        private void ConfigureAction(int keyIndex)
        {
            configurationPanel.Controls.Clear();
            configurationPanel.Controls.Add(actionConfigLabel);

            var actionDetails = keyActions[keyIndex];

            var plugin = plugins.FirstOrDefault(p => p.ActionId == actionDetails.ActionId);
            if (plugin != null)
            {
                var configControl = plugin.GetConfigurationControl();
                if (configControl != null)
                {
                    configurationPanel.Controls.Add(configControl);
                }
                return;
            }

            var inputTextBox = new TextBox
            {
                Location = new Point(10, 70),
                Size = new Size(200, 30),
                Text = actionDetails.MessageToPrint
            };
            configurationPanel.Controls.Add(inputTextBox);

            var saveButton = new Button
            {
                Text = "Save",
                Location = new Point(10, 110),
                Size = new Size(80, 30)
            };

            saveButton.Click += (s, e) =>
            {
                actionDetails.MessageToPrint = inputTextBox.Text;

                UpdateKeyAppearance(keyIndex);
                LogOutput($"Updated Key {keyIndex + 1} to {actionDetails.MessageToPrint}");
            };

            configurationPanel.Controls.Add(saveButton);
        }

        private void UpdateKeyBitmap(int keyIndex, string label)
        {
            using (Bitmap bitmap = new Bitmap(150, 150))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.FromArgb(50, 50, 50));

                    using (Font font = new Font("Segoe UI", 12, FontStyle.Bold))
                    {
                        SizeF textSize = g.MeasureString(label, font);
                        float x = (bitmap.Width - textSize.Width) / 2;
                        float y = (bitmap.Height - textSize.Height) / 2;
                        g.DrawString(label, font, Brushes.White, new PointF(x, y));
                    }
                }

                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    stream.Seek(0, SeekOrigin.Begin);

                    var keyBitmap = KeyBitmap.Create.FromStream(stream);

                    device.SetKeyBitmap(keyIndex, keyBitmap);
                }
            }

            keyButtons[keyIndex].BackColor = Color.FromArgb(50, 50, 50);
        }

        private void UpdateKeyAppearance(int keyIndex)
        {
            var action = keyActions[keyIndex];
            string text = action.ActionName;

            keyButtons[keyIndex].Text = text;
            UpdateKeyBitmap(keyIndex, text);
        }

        private void ExecuteAction(int keyIndex)
        {
            var action = keyActions[keyIndex];
            var plugin = plugins.FirstOrDefault(p => p.ActionId == action.ActionId);

            if (plugin != null)
            {
                plugin.Execute();
                LogOutput($"Executed plugin action: {plugin.Name}");
                return;
            }
        }

        private void LogOutput(string message)
        {
            outputTextBox.AppendText($"{DateTime.Now}: {message}{Environment.NewLine}");
        }
    }
}
