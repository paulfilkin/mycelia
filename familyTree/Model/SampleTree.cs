namespace Mycelia.Model;

/// <summary>A small invented family to explore the app with.  Every name here is made up.</summary>
public static class SampleTree
{
    public static TreeData Create()
    {
        var data = new TreeData { Name = "Ashworth family (sample)" };
        var doc = new TreeDocument(data);

        var census = new Source { Title = "1881 Census of England and Wales", Author = "General Register Office", Publisher = "National Archives", PublisherLocation = "Kew, Surrey, England" };
        var parish = new Source { Title = "St Mary's Parish Registers, Hebden", Author = "Church of England", Comments = "Baptisms, marriages and burials 1790-1920." };
        var bible = new Source { Title = "Ashworth Family Bible", Author = "Ashworth family", Comments = "Inscriptions on the front pages, in several hands." };
        var archive = new Repository { Name = "County Record Office", Address = "Archive Lane, Wakefield, England", Phone = "01924 000000" };
        parish.RepositoryId = archive.Id;
        census.RepositoryId = archive.Id;
        data.Repositories.Add(archive);
        data.Sources.AddRange([census, parish, bible]);

        Person P(string given, string surname, Sex sex, string birth, string birthPlace, string death = "", string deathPlace = "", string occupation = "")
        {
            var p = doc.AddPerson(given, surname, sex);
            if (birth.Length > 0 || birthPlace.Length > 0)
            {
                var b = doc.AddFact(p.Facts, FactTypes.Birth, birth, birthPlace);
                b.Citations.Add(new Citation { SourceId = parish.Id, Detail = "Baptisms, page " + (12 + p.Given.Length) });
            }
            if (death.Length > 0 || deathPlace.Length > 0)
                doc.AddFact(p.Facts, FactTypes.Death, death, deathPlace);
            if (occupation.Length > 0)
                doc.AddFact(p.Facts, FactTypes.Occupation, "", "", occupation);
            return p;
        }

        Family Couple(Person a, Person b, string date, string place, params Person[] children)
        {
            var f = doc.AddFamily(a, b);
            if (date.Length > 0 || place.Length > 0)
            {
                var m = doc.AddFact(f.Facts, FactTypes.Marriage, date, place);
                m.Citations.Add(new Citation { SourceId = parish.Id, Detail = "Marriages, entry " + (40 + children.Length) });
            }
            foreach (var c in children) doc.AddChild(f, c);
            return f;
        }

        const string hebden = "Hebden Bridge, Yorkshire, England";
        const string halifax = "Halifax, Yorkshire, England";
        const string leeds = "Leeds, Yorkshire, England";
        const string york = "York, Yorkshire, England";
        const string bristol = "Bristol, Gloucestershire, England";
        const string cork = "Cork, County Cork, Ireland";
        const string toronto = "Toronto, Ontario, Canada";
        const string manchester = "Manchester, Lancashire, England";

        // Generation 1
        var josiah = P("Josiah", "Ashworth", Sex.Male, "14 Feb 1798", hebden, "3 Nov 1871", hebden, "Hand-loom weaver");
        var hannah = P("Hannah", "Sutcliffe", Sex.Female, "Abt. 1801", halifax, "22 Jan 1866", hebden);
        var patrick = P("Patrick", "Delaney", Sex.Male, "Abt. 1805", cork, "1868", leeds, "Labourer");
        var bridget = P("Bridget", "Morrow", Sex.Female, "1809", cork, "Aft. 1881", leeds);

        // Generation 2
        var samuel = P("Samuel", "Ashworth", Sex.Male, "09 Jun 1826", hebden, "17 Aug 1894", halifax, "Mill overlooker");
        var martha = P("Martha", "Ashworth", Sex.Female, "1829", hebden, "1905", hebden);
        var john = P("John", "Ashworth", Sex.Male, "02 Mar 1832", hebden, "1899", toronto, "Carpenter");
        var mary = P("Mary Ann", "Delaney", Sex.Female, "18 Oct 1833", cork, "04 Apr 1902", halifax);
        var michael = P("Michael", "Delaney", Sex.Male, "1836", leeds, "1890", leeds, "Railway porter");

        Couple(josiah, hannah, "11 May 1824", hebden, samuel, martha, john);
        Couple(patrick, bridget, "Abt. 1831", cork, mary, michael);

        // Generation 3
        var thomas = P("Thomas", "Ashworth", Sex.Male, "21 Jul 1856", halifax, "12 Dec 1931", leeds, "Engine fitter");
        var eliza = P("Eliza Jane", "Ashworth", Sex.Female, "1859", halifax, "1860", halifax);
        var william = P("William Henry", "Ashworth", Sex.Male, "05 Jan 1862", halifax, "1938", bristol, "Schoolmaster");
        var ellen = P("Ellen", "Barraclough", Sex.Female, "30 Sep 1858", york, "08 Feb 1940", leeds);
        var grace = P("Grace", "Penrose", Sex.Female, "1864", bristol, "1949", bristol);

        Couple(samuel, mary, "26 Dec 1854", halifax, thomas, eliza, william);

        var johnsWife = P("Sarah", "Holroyd", Sex.Female, "1835", hebden, "1910", toronto);
        var jamesC = P("James", "Ashworth", Sex.Male, "1860", hebden, "1931", toronto, "Builder");
        Couple(john, johnsWife, "1858", hebden, jamesC);

        // Generation 4
        var arthur = P("Arthur", "Ashworth", Sex.Male, "14 Aug 1884", leeds, "29 Mar 1962", manchester, "Draughtsman");
        var florence = P("Florence May", "Ashworth", Sex.Female, "03 Mar 1887", leeds, "1974", leeds);
        var harold = P("Harold", "Ashworth", Sex.Male, "1890", leeds, "01 Jul 1916", "Somme, France", "Private soldier");
        var edith = P("Edith", "Kershaw", Sex.Female, "11 Nov 1886", manchester, "1970", manchester);
        var lilian = P("Lilian", "Ashworth", Sex.Female, "1890", bristol, "1981", bristol);

        Couple(thomas, ellen, "02 Apr 1883", leeds, arthur, florence, harold);
        Couple(william, grace, "1888", bristol, lilian);

        // Generation 5
        var george = P("George", "Ashworth", Sex.Male, "19 Jun 1912", manchester, "2001", manchester, "Engineer");
        var dorothy = P("Dorothy", "Ashworth", Sex.Female, "1915", manchester, "1999", york);
        var margaret = P("Margaret", "Lindley", Sex.Female, "07 Oct 1914", leeds, "2008", manchester);
        Couple(arthur, edith, "15 Sep 1910", manchester, george, dorothy);

        var peter = P("Peter", "Ashworth", Sex.Male, "23 May 1942", manchester, "", "", "Teacher");
        var susan = P("Susan", "Ashworth", Sex.Female, "1946", manchester);
        Couple(george, margaret, "1939", manchester, peter, susan);

        // A census residence and a burial for depth.
        var res = doc.AddFact(thomas.Facts, FactTypes.Residence, "3 Apr 1881", leeds, "14 Mill Street");
        res.Citations.Add(new Citation { SourceId = census.Id, Detail = "RG11/4512, folio 33, page 12" });
        doc.AddFact(josiah.Facts, FactTypes.Burial, "7 Nov 1871", hebden, "St Mary's churchyard");
        var bapt = doc.AddFact(samuel.Facts, FactTypes.Baptism, "18 Jun 1826", hebden);
        bapt.Citations.Add(new Citation { SourceId = bible.Id, Detail = "Front page" });
        doc.AddFact(harold.Facts, FactTypes.MilitaryService, "1914", "", "West Yorkshire Regiment");
        doc.AddFact(john.Facts, FactTypes.Emigration, "1870", "Liverpool, Lancashire, England", "Sailed for Canada");

        thomas.Notes = "Thomas served his apprenticeship at a locomotive works and moved the family to Leeds around 1880.";
        harold.Notes = "Remembered on the town war memorial.";

        // The standard historical events, so the Person view's timeline has something to show.
        data.HistoricalEvents.AddRange(Timeline.StandardEvents());
        data.HomePersonId = arthur.Id;
        return data;
    }
}
