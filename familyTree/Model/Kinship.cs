namespace Mycelia.Model;

/// <summary>One person on the path between two relatives.</summary>
public sealed record KinshipStep(Person Person, string Relation);

public sealed record KinshipResult(string Text, IReadOnlyList<KinshipStep> Path);

/// <summary>
/// Names how one person is related to another ("1st cousin 2x removed", "father-in-law").
/// Wording follows Design considerations, Relationship names.
/// </summary>
public static class Kinship
{
    private enum Step { Up, Down, Spouse }

    private sealed record Ancestor(int Depth, string? PrevId, string? ViaFamilyId, Sex FirstParentSex);

    /// <summary>"other is ego's ..."</summary>
    public static KinshipResult Describe(TreeDocument doc, Person ego, Person other, bool withPath = true)
    {
        if (ego == other) return new KinshipResult("self", [new KinshipStep(ego, "self")]);

        var blood = DescribeBlood(doc, ego, other);
        if (blood is not null)
        {
            var path = withPath ? BuildPath(doc, ego, blood.Value.PathIds) : [];
            return new KinshipResult(blood.Value.Text, path);
        }

        var chain = ShortestChain(doc, ego, other);
        if (chain is null) return new KinshipResult("unrelated", []);
        var text = NameChain(doc, chain);
        var ids = chain.Select(c => c.Id).Prepend(ego.Id).ToList();
        return new KinshipResult(text, withPath ? BuildPath(doc, ego, ids) : []);
    }

    private static List<KinshipStep> BuildPath(TreeDocument doc, Person ego, IReadOnlyList<string> ids)
    {
        var steps = new List<KinshipStep>();
        foreach (var id in ids)
        {
            var p = doc.GetPerson(id);
            if (p is null) continue;
            var rel = p == ego ? "self" : Describe(doc, ego, p, withPath: false).Text;
            steps.Add(new KinshipStep(p, rel));
        }
        return steps;
    }

    // ---- blood relatives through a common ancestor ----

    private static Dictionary<string, Ancestor> Ancestors(TreeDocument doc, Person start)
    {
        var map = new Dictionary<string, Ancestor> { [start.Id] = new Ancestor(0, null, null, Sex.Unknown) };
        var queue = new Queue<Person>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            var here = map[p.Id];
            if (here.Depth > 40) continue;
            foreach (var fam in doc.ParentFamilies(p))
            {
                var link = fam.Children.First(c => c.PersonId == p.Id);
                foreach (var (pid, nature) in new[] { (fam.Partner1Id, link.Nature1), (fam.Partner2Id, link.Nature2) })
                {
                    if (pid is null || map.ContainsKey(pid)) continue;
                    // Step and guardian links are not blood; those relatives are named as step relations.
                    if (nature is ChildNature.Step or ChildNature.Guardian or ChildNature.Foster) continue;
                    var parent = doc.GetPerson(pid);
                    if (parent is null) continue;
                    var firstSex = here.Depth == 0 ? parent.Sex : here.FirstParentSex;
                    map[pid] = new Ancestor(here.Depth + 1, p.Id, fam.Id, firstSex);
                    queue.Enqueue(parent);
                }
            }
        }
        return map;
    }

    private static (string Text, List<string> PathIds)? DescribeBlood(TreeDocument doc, Person ego, Person other)
    {
        var mine = Ancestors(doc, ego);
        var theirs = Ancestors(doc, other);
        var best = int.MaxValue;
        string? bestId = null;
        foreach (var (id, a) in mine)
        {
            if (!theirs.TryGetValue(id, out var b)) continue;
            var total = a.Depth + b.Depth;
            if (total < best || (total == best && bestId is not null && Math.Max(a.Depth, b.Depth) < Math.Max(mine[bestId].Depth, theirs[bestId].Depth)))
            {
                best = total;
                bestId = id;
            }
        }
        if (bestId is null) return null;

        var up = mine[bestId].Depth;
        var down = theirs[bestId].Depth;

        // The children of the common ancestor on each side: full relatives when they share a family.
        var half = false;
        if (up > 0 && down > 0)
        {
            var myFamily = FamilyBelow(mine, bestId);
            var theirFamily = FamilyBelow(theirs, bestId);
            half = myFamily != theirFamily;
        }

        var side = up >= 1 ? SideOf(mine, bestId) : Sex.Unknown;
        var text = BloodName(up, down, other.Sex, half, side);

        // Path: ego up to the ancestor, then down to the other person.
        var pathIds = new List<string>();
        var cur = (string?)bestId;
        while (cur is not null) { pathIds.Add(cur); cur = mine[cur].PrevId; }
        pathIds.Reverse();
        pathIds.AddRange(WalkDown(theirs, bestId, other.Id));
        return (text, pathIds);
    }

    /// <summary>People from just below the ancestor down to the target, in order.</summary>
    private static List<string> WalkDown(Dictionary<string, Ancestor> map, string ancestorId, string targetId)
    {
        // map holds, for each ancestor of target, the person one step closer to target (PrevId).
        var list = new List<string>();
        var cur = map[ancestorId].PrevId;
        while (cur is not null)
        {
            list.Add(cur);
            if (cur == targetId) break;
            cur = map[cur].PrevId;
        }
        return list;
    }

    private static string? FamilyBelow(Dictionary<string, Ancestor> map, string ancestorId) =>
        map[ancestorId].ViaFamilyId;

    private static Sex SideOf(Dictionary<string, Ancestor> map, string ancestorId) => map[ancestorId].FirstParentSex;

    private static string Gendered(Sex sex, string male, string female, string neutral) =>
        sex switch { Sex.Male => male, Sex.Female => female, _ => neutral };

    /// <summary>Ordinal: 1st, 2nd, 3rd, 4th, 11th, 21st...</summary>
    public static string Ordinal(int n)
    {
        var tens = n % 100;
        var suffix = tens is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    private static string Greats(int generationsAboveGrand) =>
        generationsAboveGrand switch
        {
            <= 0 => "",
            1 => "great ",
            _ => $"{Ordinal(generationsAboveGrand)} great ",
        };

    /// <summary>Name for a blood relative <paramref name="up"/> generations above ego's line and <paramref name="down"/> below.</summary>
    public static string BloodName(int up, int down, Sex sex, bool half, Sex side)
    {
        var halfText = half ? "half " : "";
        if (up == 0)
        {
            if (down == 1) return Gendered(sex, "son", "daughter", "child");
            var grand = Gendered(sex, "grandson", "granddaughter", "grandchild");
            return Greats(down - 2) + grand;
        }
        if (down == 0)
        {
            if (up == 1) return Gendered(sex, "father", "mother", "parent");
            var grand = Gendered(sex, "grandfather", "grandmother", "grandparent");
            if (up == 2)
            {
                var sideText = side switch { Sex.Male => "paternal ", Sex.Female => "maternal ", _ => "" };
                return sideText + grand;
            }
            return Greats(up - 2) + grand;
        }
        if (up == 1 && down == 1)
            return halfText + Gendered(sex, "brother", "sister", "sibling");
        if (up == 1)
        {
            var nephew = Gendered(sex, "nephew", "niece", "nephew/niece");
            return down switch
            {
                2 => halfText + nephew,
                3 => halfText + "grand " + nephew,
                4 => halfText + "great grand " + nephew,
                _ => halfText + $"{Ordinal(down - 3)} great grand " + nephew,
            };
        }
        if (down == 1)
        {
            var uncle = Gendered(sex, "uncle", "aunt", "uncle/aunt");
            return up switch
            {
                2 => halfText + uncle,
                3 => halfText + (half ? "great " : "great-") + uncle,
                _ => halfText + $"{Ordinal(up - 2)} great-" + uncle,
            };
        }
        var degree = Math.Min(up, down) - 1;
        var removed = Math.Abs(up - down);
        var cousin = $"{Ordinal(degree)} cousin";
        if (removed > 0) cousin += $" {removed}x removed";
        else if (degree == 1 && !half && side != Sex.Unknown) cousin = (side == Sex.Male ? "paternal " : "maternal ") + cousin;
        return halfText + cousin;
    }

    // ---- relatives through marriage ----

    private sealed record ChainStep(string Id, Step Step);

    /// <summary>Shortest chain of parent, child and spouse links (breadth first).</summary>
    private static List<ChainStep>? ShortestChain(TreeDocument doc, Person ego, Person other)
    {
        var prev = new Dictionary<string, ChainStep?> { [ego.Id] = null };
        var prevId = new Dictionary<string, string>();
        var queue = new Queue<Person>();
        queue.Enqueue(ego);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p == other) break;
            foreach (var (next, step) in Neighbours(doc, p))
            {
                if (prev.ContainsKey(next.Id)) continue;
                prev[next.Id] = new ChainStep(next.Id, step);
                prevId[next.Id] = p.Id;
                queue.Enqueue(next);
            }
        }
        if (!prev.ContainsKey(other.Id)) return null;
        var chain = new List<ChainStep>();
        var cur = other.Id;
        while (cur != ego.Id)
        {
            chain.Add(prev[cur]!);
            cur = prevId[cur];
        }
        chain.Reverse();
        return chain;
    }

    private static IEnumerable<(Person, Step)> Neighbours(TreeDocument doc, Person p)
    {
        foreach (var parent in doc.Parents(p)) yield return (parent, Step.Up);
        foreach (var fam in doc.SpouseFamilies(p))
        {
            if (doc.GetPerson(fam.OtherPartner(p.Id)) is { } spouse) yield return (spouse, Step.Spouse);
            foreach (var child in doc.Children(fam)) yield return (child, Step.Down);
        }
    }

    private sealed record Term(string Text, string Kind, Sex Sex, int Up, int Down);

    private static string NameChain(TreeDocument doc, List<ChainStep> chain)
    {
        // Split into blood segments (up* then down*) separated by spouse links.
        var terms = new List<Term>();
        var up = 0;
        var down = 0;
        Person? last = null;
        void Flush()
        {
            if (up == 0 && down == 0 || last is null) return;
            var text = BloodName(up, down, last.Sex, false, Sex.Unknown);
            var kind = (up, down) switch
            {
                (1, 0) => "parent",
                (0, 1) => "child",
                (1, 1) => "sibling",
                _ => "blood",
            };
            terms.Add(new Term(text, kind, last.Sex, up, down));
            up = down = 0;
        }

        foreach (var step in chain)
        {
            var person = doc.GetPerson(step.Id)!;
            switch (step.Step)
            {
                case Step.Spouse:
                    Flush();
                    terms.Add(new Term(Gendered(person.Sex, "husband", "wife", "spouse"), "spouse", person.Sex, 0, 0));
                    break;
                case Step.Up:
                    if (down > 0) Flush();
                    up++;
                    break;
                case Step.Down:
                    down++;
                    break;
            }
            last = person;
        }
        Flush();

        // Combine common pairs into in-law and step words.
        var merged = new List<string>();
        for (var i = 0; i < terms.Count; i++)
        {
            var t = terms[i];
            var n = i + 1 < terms.Count ? terms[i + 1] : null;
            var n2 = i + 2 < terms.Count ? terms[i + 2] : null;
            if (t.Kind == "parent" && n?.Kind == "spouse" && n2?.Kind == "child")
            {
                merged.Add(Gendered(n2.Sex, "stepbrother", "stepsister", "step sibling"));
                i += 2;
            }
            else if (t.Kind == "spouse" && n?.Kind == "parent")
            {
                merged.Add(Gendered(n.Sex, "father-in-law", "mother-in-law", "parent-in-law"));
                i++;
            }
            else if (t.Kind == "spouse" && n?.Kind == "sibling")
            {
                merged.Add(Gendered(n.Sex, "brother-in-law", "sister-in-law", "sibling-in-law"));
                i++;
            }
            else if (t.Kind == "sibling" && n?.Kind == "spouse")
            {
                merged.Add(Gendered(n.Sex, "brother-in-law", "sister-in-law", "sibling-in-law"));
                i++;
            }
            else if (t.Kind == "child" && n?.Kind == "spouse")
            {
                merged.Add(Gendered(n.Sex, "son-in-law", "daughter-in-law", "child-in-law"));
                i++;
            }
            else if (t.Kind == "parent" && n?.Kind == "spouse")
            {
                merged.Add(Gendered(n.Sex, "stepfather", "stepmother", "stepparent"));
                i++;
            }
            else if (t.Kind == "spouse" && n?.Kind == "child")
            {
                merged.Add(Gendered(n.Sex, "stepson", "stepdaughter", "stepchild"));
                i++;
            }
            else
            {
                merged.Add(t.Text);
            }
        }

        // "husband of 1st cousin": the last term first, joined with " of ".
        merged.Reverse();
        return string.Join(" of ", merged);
    }
}
