# Mycelia - design considerations

What Mycelia is for, where it will feel familiar, where it is different, and the conventions it
follows for dates, names, relationships, data checks and sorting.  How it is built is in the
design record, `TDD_Mycelia_v0_2_0.md`.  Code that follows one of the conventions below names its
section here ("Design considerations, Dates").

## Familiar ground

Mycelia will feel familiar to people used to working with Family Tree Maker and, in much of it,
to users of other desktop genealogy programs.  The tree is people, families and facts, each fact
with a date, a place, a description and sources.  The People workspace has an index, a tree and a
person panel, and a Person view with the full facts table.  Dates are written the way
genealogists write them ("Abt. 1850", "Bef. 1900", "Bet. 1850-1855"), relationships are named the
usual way ("2nd cousin 1x removed"), unlikely dates are flagged, and the common keyboard shortcuts
work.  Someone moving a tree across with GEDCOM should find their way round without a manual.

## What is different

- **Your tree stays yours.**  It is a file on your computer, in an open, readable format.  There
  is no account, no subscription and no cloud copy.  GEDCOM 5.5.1 moves a tree in and out.
- **Its own program.**  Mycelia's code, data model, file format, graphics and icons are its own,
  and it is open source.
- **AI-based research help.**  The Assistant reads the tree, works out what is missing, suggests
  where to look, searches record archives and catalogues through MCP servers and open archive
  APIs, downloads records, and adds facts with citations: the web address, the date it was
  looked at and an extract.  Work that used to take hours of searching, copying and typing takes
  minutes, and the researcher stays in charge: every change says what it will do and waits for
  approval, every change can be undone, and a whole request can be reverted.
- **Choice of AI model.**  Anthropic, OpenAI, or any service with the OpenAI chat interface,
  including local ones where nothing leaves the computer.
- **Privacy by default.**  The AI model gets only the names of living people and nothing about
  anything marked Private, unless the person changes that.  Records are searched only for people
  who have died.
- **Connected both ways.**  Archives' own MCP servers can be added in a few clicks, and other
  programs (Claude Desktop, Claude Code) can use Mycelia's tools, with the same approvals.
- **Not built:** web search, publishing and sync with an online service, hints from a
  subscription site, and reports beyond the four charts.

## Enumerations

- Couple types: Spouse, Partner, Friend, Single, Other, Unknown, Private.
- Couple status: Unknown, Ongoing, Divorced, Deceased, Separated, Annulled, Private, Other, None.
- Parent-child nature: Natural (biological), Adopted, Step, Foster, Related, Guardian, Sealed, Private, Unknown.
- Sex: Male, Female, Unknown.

## Fact types

Person, with date, place and description: Birth, Death, Adoption, Baptism, Bar Mitzvah, Bat Mitzvah, Blessing, Burial, Census, Christening, Christening (adult), Circumcision, Confirmation, Cremation, Degree, Education, Elected, Emigration, Employment, Excommunication, First Communion, Funeral, Graduation, Immigration, Military Service, Naturalization, Ordination, Probate, Property, Retirement, Will, Address, Email, Web Address, Phone Number, Military Serial Number, Medical Condition, DNA Markers, Occupation, Residence, Physical Description, Arrival, Departure.

Person, description only: Also Known As, Title, Caste, Person ID, Height, Weight, Cause of Death, Nationality, Religion, Namesake, Social Security Number, Destination, Origin.

Couple: Marriage, Annulment, Divorce, Divorce Filed, Engagement, Separation, Marriage License, Marriage Contract, Marriage Bann, Marriage Settlement.

Each type has a fixed id, kept in the tree file, and a GEDCOM tag (BIRT, DEAT, MARR, BURI, OCCU, RESI ...); types with no standard tag use custom ones (_MILT ...).  LDS ordinance types are not in the list.  People can add their own types.

## Dates

- Modifiers: Before "Bef.", After "Aft.", About "Abt.", Calculated "Cal.", double dates, and dates with the year, month or day missing.
- Ranges: "Bet. {from}-{to}".  If both ends share a year, the first year is dropped; if they also share a month, the first month is dropped (DMY order).
- Standard format "DMY": `mod dd Mmm yyyy` with a two-digit day ("05 Mar 1890").  Other formats: dMY (no leading zero), LDMY (full month), MDY ("Mar 5, 1890"), LMDY.
- Missing parts are omitted and spaces collapsed.
- Years up to 999 get "AD"; negative years get "BC".  Double dates print as "1720/21".
- Age (date calculator, ages shown with facts): years = y2-y1, less one if the anniversary has not been reached; months = (m2-m1) mod 12, less one if d2 < d1; days = d2-d1, or borrowed from the previous month's length.  Leap year: divisible by 4, except centuries not divisible by 400.
- Date calculator modes: Birth (event date minus age), Event (birth date plus age), Age (between two dates).  Each date shows its weekday.

## Names

- Unknown name: "<No name>".
- Formats: FML "Given Surname Suffix"; LFM (reverse, used in the index) "Surname, Given Suffix"; FL "First Surname"; fL "F. Surname"; fmL "J.W. Surname"; L "Surname".  Optional surname in capitals.

## Relationship names (kinship)

Ordinal for great-grandparents = generations - 2; cousin degree = min(up, down) - 1; removed = |up - down|.

Path letters: P/F/M parent, C/S/D child, G/B/Z sibling, E/H/W spouse.  Wording used:

- Direct: father, mother, parent, son, daughter, child, husband, wife, spouse, brother, sister, sibling, self.
- Grandparents: paternal/maternal grandfather/grandmother (2 generations); great grandfather (3); "{n}nd great grandfather" from 4 (n = generations - 2).  Same for grandchildren.
- Uncle/aunt (2 up, 1 down), great-uncle/great-aunt (3 up), "2nd great-uncle" from 4 up.
- Nephew/niece (1 up, 2 down), grand nephew/niece (3 down), great grand nephew (4 down), "{n} great grand nephew" from 5.
- Cousins: degree = min(up, down) - 1, removed = |up - down|: "1st cousin", "2nd cousin 1x removed".  First cousins through the father or mother: "paternal 1st cousin" / "maternal 1st cousin".
- Half: prefix "half" when the linking siblings share one parent (half brother, half 1st cousin, half nephew).
- In-law and step: father-in-law, mother-in-law, brother-in-law, sister-in-law, son-in-law, daughter-in-law, stepfather, stepmother, stepson, stepdaughter, stepchild, stepbrother, stepsister.
- Longer chains join terms with " of ": "husband of 1st cousin".
- No path: "unrelated".

## Data error checks

### Constants

- Parent of-age minimum: 13 years.
- Mother of-age maximum: 60 years.  Father of-age maximum: 80 years.
- Questionable age at death: above 120 years.
- Father's death allowed before a child's birth: 330 days.  Mother: 0 days.

### Comparing two dates

Both dates must be real calendar dates (not text, not ranges) with years.  They are compared only to the precision both share: months only when both have a month; days only when both have a month and a day.  The result is the signed difference from the first date to the second, in days and in whole years.

- Before(a, b, n): days from a to b > n.
- After(a, b): days from a to b < 0.
- Too young / too old: years from a to b < or > the limit.
- Child versions: the same with the years negated.

### Checks run for each fact

- Birth: after own death; mother under 13 or over 60 at the birth; birth after mother's death; father under 13 or over 80; birth more than 330 days after father's death; birth after any other preferred fact of the person or their couples; married under 13 (only when there is a spouse); age at death over 120; for each child: parent born after the child, parent less than 13 years older than the child, parent born more than 13 years after the child.
- Death: age over 120; marriage after death; burial before death; death before a child's birth (more than 330 days for a father, any time for a mother).
- Burial: before death.
- Marriage: after own death; after the couple's divorce; after the spouse's death; under 13; spouse under 13.
- Divorce: before the couple's marriage.
- Every fact: before birth; date not a recognised date; no date, place or description.

### Duplicate facts

- Types a person has once only: two of them are always flagged.  Type ids 1, 2, 3, 8, 9, 11, 13, 17, 19, 20, 27, 30, 31, 40, 43, 48, 54, 59, 82, 88 (Name, Birth, Death, Baptism (LDS), Bar Mitzvah, Bat Mitzvah, Burial, Circumcision, Confirmation (LDS), Cremation, Endowment (LDS), First Communion, Funeral, Probate, Sealed to Parents (LDS), Sex, Military Serial Number, DNA Markers, Social Security Number, Initiatory (LDS)).
- Other types: two facts of one type are duplicates when every detail both have agrees.  Dates must be under 60 days apart.  Places and descriptions must have an edit distance (Levenshtein) under half the longer text, at most 20, or one must contain the other.  Descriptions are compared ignoring case.

### Person checks

- Name with a word starting or ending in a dash.
- Name containing any of `! @ # $ % ^ & * + = { } | ; : / ~`.
- Same surname as the preferred spouse (ignoring "?" names).
- Birth date missing; marriage date missing when the preferred couple is of type Spouse.
- Children of the preferred couple not in birth order.
- Sex unknown; person with no spouse, parents or children.
- Preferred parents are chosen with Make preferred in the Family tab; until they are, the first family listing the person as a child counts, so there is no "preferred spouse or parents not set" check.

### Relationship checks

These are Mycelia's own, under the category Relationships.

- The same couple recorded as two families: "Recorded twice as a couple with {spouse}."  Combine with... in the Family tab makes them one.
- A child linked as Natural to the same parent in two sets of parents: "Natural child of {parent} in two families."  It usually means one family has the other parent missing and should be combined with the couple's family.
- A record with no name, or with only "?" or "Unknown" as its names: "Has no name, or only "?" or "Unknown"."  Some programs record an unknown spouse this way.  Such a record is a placeholder, not a person: give it the name once it is known, or use Make unknown spouse to leave the spouse unknown.  The Assistant names such a record rather than adding the person again.
- A family with no partners is not flagged: Add sibling makes one to hold siblings whose parents are not recorded.

## Index sorting

- Sort orders: family name, given name, birth date, marriage date, death date.  There is no sort by place.
- Family name: by the reversed name "Surname, Given Suffix", then by birth date.
- Given name: by "Given Surname Suffix", then by birth date.
- Birth, marriage or death date: by that date, then by the reversed name.
- People without the date sort after everyone with one.  The preferred birth, death or marriage is used.
- Names compare as text.

## History

- At most 400 entries.  When full, the oldest is removed before a new one is added.
- Entries are stored with the tree.
- The History list shows each person once, sorted with the index's current sort order.

## Person view: the facts table

- Groups: "Personal information", "Individual facts", then "Shared facts with {spouse}" for each
  couple.  Groups with no facts are still shown.
- Personal information holds the types Name (1), Sex (48), Title (47), Also Known As (46),
  Ancestral File Number (85), Person ID (53) and FamilySearch ID (92).  Every other personal fact
  is an individual fact.  Couple facts go under their couple.
- Couples follow the person's spouse order when it is set, otherwise their preferred Marriage
  date, undated last.
- Columns: Fact, Date, Place / Description, Sources (count of citations), Notes (1 when the fact
  has a note), Preferred.
- Default order: the Date column, ascending, chronologically: Name and Sex first; then by date
  with undated facts last; facts on the same date, or both undated, by type rank.
- Type rank: Name 0, Sex 1, Birth 2, Baptism 3, Blessing 4, Christening 5, Circumcision 6,
  Endowment (LDS) 7, Marriage 8, Annulment 9, Divorce Filed 10, Divorce 11, then Death 9999,
  Funeral 10000, Cremation 10001, Burial 10002.  Any other type ranks by its type number.
- Clicking a column heading sorts by that column; clicking it again reverses the order.  Name and
  Sex stay at the top of Personal information whichever column is sorted.
- The Preferred column reads "Preferred" only on the preferred fact of its type, and is otherwise
  blank.  A private fact shows a lock, except Sex.

## Living people

Steps, in order:

1. Any death date or death place: not living.
2. Living so far when the birth year is unknown or within the last 120 years, and the preferred
   marriage year is unknown or within the last 100 years.
3. A death-related fact (types 20 Cremation, 13 Burial, 31 Funeral, 57 Cause of Death): not living,
   and relatives are not asked.
4. Living by themselves needs a birth date.
5. Otherwise living when the preferred spouse, or any parent in any of their parent families, is
   living by the same steps without asking their own relatives.  Every parent counts, not only the
   preferred ones, so that more people are treated as living rather than fewer.

The rule decides who is privatised on export (given name "Living", surname and sex kept, nothing
else) and how much the Assistant's AI model is told about a person.

## Convert names

- Two choices: "First Middle Surname" and "First Middle SURNAME".
- Every word is lower case with a capital first letter, except:
  - Mc names longer than five letters keep the third letter in capitals ("McDonald").
  - Mac names longer than six letters keep the fourth in capitals ("MacKenzie"; "Mack" is left as
    "Mack").
  - O' names longer than five letters keep the third in capitals ("O'Brien").
  - Dotted initials (`^([a-zA-Z]\.){2,}$`, "J.R.") stay in capitals.
  - Single letters are capitals.
  - A leading bracket or quote is kept and the next letter is the first letter.
  - In given names, "of" and "or" are lower case.
- Each part of a hyphenated name, given or surname, is converted on its own.
- A name is converted only when it needs it: some word of three or more characters has a first
  character that is not a capital, or a second or third that is not lower case, with extra checks
  on a word starting with "/".  A surname of two or more letters written "/Surname/" always passes
  the second-character test, so in practice every name with a surname is converted and only names
  without one can be skipped.
- The suffix is left alone, and spacing round brackets is kept as it is.

## Spouse order

A person's spouses are kept in an order set with Set spouse order; the first is the preferred
spouse, shown with the person, and the family panel, Person view and spouse lists follow the
order.

## Charts

- Pedigree, descendant, fan and family group sheet.
- Default generations: 4, from 2 to 7; the descendant chart shows at most five.

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Help for the screen in front | F1 |
| Open, New, Save | Ctrl+O, Ctrl+N, Ctrl+S |
| Undo, Redo | Ctrl+Z, Ctrl+Y |
| Back, Forward | Alt+Left, Alt+Right |
| Person view | F3 |
| Find and replace | Ctrl+F |
| Tools (date calculator) | Ctrl+D |
| Go to the home person | Ctrl+Home |
| Bookmark the person | Ctrl+Shift+B |
| Home, People, Places, Media, Sources, Charts, Tools | Ctrl+1 to Ctrl+7 |
