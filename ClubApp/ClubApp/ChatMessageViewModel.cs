namespace AetherShell.Client
{
    public class ChatMessageViewModel
    {
        public string Text { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsUser => !IsAdmin;
    }
}