using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

static class UiPaintTests
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Assembly assembly = Assembly.LoadFrom(args[0]);
            Type panelType = assembly.GetType("Aion2CNTool.GradientPanel", true);
            MethodInfo paint = panelType.GetMethod("OnPaintBackground", BindingFlags.Instance | BindingFlags.NonPublic);
            Size[] sizes = { new Size(0, 116), new Size(840, 0), Size.Empty, new Size(1, 1), new Size(840, 116), new Size(1260, 174), new Size(1680, 232) };
            using (var bitmap = new Bitmap(1680, 232))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var panel = (Panel)Activator.CreateInstance(panelType, true))
            {
                foreach (Size size in sizes)
                {
                    panel.Size = size;
                    paint.Invoke(panel, new object[] { new PaintEventArgs(graphics, new Rectangle(Point.Empty, size)) });
                    Console.WriteLine("PASS background paint " + size);
                }
            }
            Type formType = assembly.GetType("Aion2CNTool.MainForm", true);
            using (var form = (Form)Activator.CreateInstance(formType, true))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-32000, -32000);
                form.Show();
                Application.DoEvents();
                foreach (Size size in new[] { new Size(840, 620), new Size(780, 570), new Size(1260, 930) })
                {
                    form.ClientSize = size;
                    form.Refresh();
                    Application.DoEvents();
                    using (var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.ClientSize));
                        if (args.Length > 1 && size == new Size(840, 620))
                            image.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Console.WriteLine("PASS form startup/resize/render " + size);
                }
                form.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                form.WindowState = FormWindowState.Normal;
                form.Refresh();
                Application.DoEvents();
                Console.WriteLine("PASS minimize/restore");
                form.Close();
            }
            Console.WriteLine("PASS UI paint regression; no installation or restore invoked.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
