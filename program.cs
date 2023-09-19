public class Game
{
    private string title;
    private int releaseYear;

    public Game(srting title, int releaseYear)
    {
        this.title = title
        this.releaseYear = releaseYear
    }

    public void DisplayInfo()
    {
        Console.WriteLine("Название игры: " + title);
        Console.WriteLine("Год выпуска: " + releaseYear);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Game game = new Game("Crysis", 2007);

        game.DisplayInfo();
    }
}