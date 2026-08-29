namespace AetherShell.Client
{
    public class GameModel
    {
        public string Title { get; set; }       // Название 
        public string ExePath { get; set; }     // Путь к exe 
        public string IconPath { get; set; }    // Путь к картинке 
        public string Args { get; set; }        // Аргументы запуска
    }
}