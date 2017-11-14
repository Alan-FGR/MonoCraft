using System;
public static class Program
{
   [STAThread] static void Main()
   {
      using (var game = new MonoCraft()) game.Run();
   }
}
