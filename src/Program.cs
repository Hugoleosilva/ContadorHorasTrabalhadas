using System;
using System.Threading;
using System.Windows.Forms;

namespace RelogioAcompanhamento
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool demoMode = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--demo-limits", StringComparison.OrdinalIgnoreCase))
                    {
                        demoMode = true;
                    }
                }
            }

            string mutexName = demoMode
                ? "Local\\RelogioAcompanhamento_Demo_SingleInstance"
                : "Local\\RelogioAcompanhamento_SingleInstance";

            bool createdNew;
            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Já existe uma instância rodando: traz/alterna a janela existente
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOW_EXISTING, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                try
                {
                    NativeMethods.SetProcessDPIAware();
                }
                catch
                {
                    // Ignora se já configurado
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (TrayApplicationContext context = new TrayApplicationContext(demoMode))
                {
                    Application.Run(context);
                }
            }
        }
    }
}
