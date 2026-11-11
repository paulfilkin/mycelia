# Genealogy research resources - worldwide

Compiled 10 November 2026.

## How to read this list

- **Cost**: Free, Free (registration), Freemium (free search, paid records or features), Paid (subscription), Pay per view.
- **API**: a documented interface for programmatic access.  "None public" means no documented public API.
- **MCP**: Model Context Protocol server availability.
  - *Official* - published by the resource owner.
  - *Community* - third-party, unofficial.  Check the resource's terms of use before relying on one.
  - *None known* - nothing surfaced in MCP directory searches on the compile date.  This was not verified resource by resource for the smaller regional sites.
- **†** marks API details taken from prior knowledge and not rechecked on the compile date.  Everything else in the API and MCP columns was checked against the sources listed at the end.
- Prices are omitted because they change often.

## Summary of API and MCP findings

- No large commercial platform (Ancestry, MyHeritage, Findmypast) has an official MCP server or an open public API for record search.
- FamilySearch has a free API, but access needs an approved application and FamilySearch states that access for individual personal use is not granted.  Community MCP servers exist; some work by reusing browser session cookies and not the approved API.
- Two record providers publish their own MCP server: Riksarkivet (Swedish National Archives, beta from September 2026 to end of 2026) and Open Archives (Netherlands and Belgium, published under the operator's GitHub account).
- Open-data national institutions are the best-served for automation: The National Archives (UK), NARA and Library of Congress (US), Trove (Australia), DigitalNZ, Deutsche Digitale Bibliothek, Riksarkivet, Europeana.
- For your own tree data, Gramps Web, WikiTree and plain GEDCOM files each have several community MCP servers.

---

## 1. Global platforms and collaborative trees

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| FamilySearch | https://www.familysearch.org | Search indexed records and browse unindexed images; shared single Family Tree; Full-Text Search of handwritten records; catalogue of microfilmed records | Worldwide; strongest free coverage for the Americas, Europe, Pacific, parts of Africa and Asia | Free (registration) | Yes, free.  Requires approved application; not granted for individual personal use | Community only (several, e.g. JonathanReiss15/familysearch-mcp, ball2jh/familysearch-org-mcp, hosted MCPBundles) | Some images viewable only at FamilySearch Centres or affiliate libraries.  Some community MCP servers use browser session cookies, which is outside the approved API route |
| Ancestry | https://www.ancestry.com | Record search, private member trees, hints, DNA matching | Worldwide; strongest for US, UK, Ireland, Canada, Australia, Germany | Paid; some free collections | None public † | None official.  "ancestry-mcp" projects only read GEDCOM files | Owns Find a Grave, Fold3, Newspapers.com, Geneanet.  Often free on site at public libraries |
| MyHeritage | https://www.myheritage.com | Record search, trees, Smart Matches, DNA, photo tools | Worldwide; strong for continental Europe, Scandinavia, Jewish records | Freemium / Paid | Family Graph API launched 2011; current availability not confirmed | None official | Owns Geni and Filae.  Interface in over 40 languages |
| Findmypast | https://www.findmypast.co.uk | Record search, trees, newspapers | UK, Ireland; also US, Canada, Australia, NZ | Paid; some free collections | None public † | None known | Exclusive or early access to many British parish and census sets, including the 1921 Census of England and Wales |
| Geneanet | https://www.geneanet.org | User trees, indexed records, cemetery photos, digitised books | France and continental Europe | Freemium | None public † | None known | Owned by Ancestry.  Trees searchable free |
| Geni | https://www.geni.com | Collaborative single world tree, curated by volunteers | Worldwide | Freemium | Yes, REST API with OAuth † | None known | Owned by MyHeritage |
| WikiTree | https://www.wikitree.com | Collaborative single tree with sourced biographies; DNA connections | Worldwide; English-language bias | Free | Yes, read-only, free.  Rate limited | Community (PeWu/wikitree-mcp, Genealogy-MCP/wikitree-mcp) | Public profiles readable without authentication |
| Wikidata | https://www.wikidata.org | Structured data on notable people, places, family relations; link identifiers across sites | Worldwide | Free | Yes, SPARQL endpoint and REST API, open | Community (e.g. cyanheads/wikidata-mcp-server, zzaebok/mcp-wikidata) | Only notable individuals.  Useful for nobility and public figures |
| Find a Grave | https://www.findagrave.com | Search memorials, headstone photos, family links | Worldwide; strongest for US | Free | None public; terms prohibit scraping | None official | Owned by Ancestry.  Volunteer-contributed, variable accuracy |
| BillionGraves | https://billiongraves.com | GPS-tagged headstone photos and transcriptions | Worldwide | Freemium | By application †; not open | None known | Mobile app for capturing cemeteries |
| Forebears | https://forebears.io | Surname distribution and meaning; directory of sources by place | Worldwide | Free | Paid API †; not rechecked | None known | Good first step for locating where a surname clusters |

## 2. DNA testing and analysis

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| AncestryDNA | https://www.ancestry.com/dna | Autosomal test; match list linked to trees | Worldwide; largest database, US and UK heavy | Paid test; subscription for full features | None public † | None known | Does not accept uploads from other companies |
| 23andMe | https://www.23andme.com | Autosomal test with haplogroups; relative matching | Worldwide; US heavy | Paid | Public API withdrawn † | None known | Ownership changed in 2025 after bankruptcy proceedings; check current data policy before testing |
| MyHeritage DNA | https://www.myheritage.com/dna | Autosomal test; accepts uploads; chromosome browser | Worldwide; strong in Europe | Paid | None public † | None known | Useful for continental European matches |
| FamilyTreeDNA | https://www.familytreedna.com | Y-DNA, mitochondrial and autosomal tests; surname projects | Worldwide | Paid | None public † | None known | Main provider for deep paternal-line (Big Y) testing |
| Living DNA | https://livingdna.com | Autosomal test with regional breakdown | UK and Ireland regional detail | Paid | None public † | None known | Accepts uploads |
| GEDmatch | https://www.gedmatch.com | Upload raw data from any company; cross-company matching and analysis tools | Worldwide | Freemium | None public † | None known | Review the law-enforcement matching opt-in settings |
| DNA Painter | https://dnapainter.com | Chromosome mapping; Shared cM tool for relationship estimates | Worldwide | Freemium | None public † | None known | Analysis tool only, no testing |

## 3. United Kingdom and Ireland

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| General Register Office (GRO) | https://www.gro.gov.uk | Search birth and death indexes (with mother's maiden name and age at death); order certificates or digital images | England and Wales | Index free (registration); copies paid | None public † | None known | Civil registration from 1837 |
| FreeBMD / FreeREG / FreeCEN | https://www.freebmd.org.uk | Volunteer transcriptions of civil registration indexes, parish registers and censuses | England and Wales; some Scotland | Free | None public † | None known | Run by Free UK Genealogy; open data ethos |
| The National Archives - Discovery | https://discovery.nationalarchives.gov.uk | Catalogue of TNA and over 2,500 other UK archives; download digitised records (wills, service records) | UK | Free search; some downloads paid | Yes, open, no authentication | Community (hosted by MCPBundles, 4 tools) | API searches descriptions and titles, not the text inside digitised documents.  Terms ask users not to cache API content |
| ScotlandsPeople | https://www.scotlandspeople.gov.uk | Statutory registers, old parish registers, census, wills | Scotland | Pay per view (credits); index search free | None public † | None known | Official government source; images of original entries |
| IrishGenealogy.ie | https://www.irishgenealogy.ie | Civil registration images and church records | Ireland | Free | None public † | None known | State-run |
| National Archives of Ireland - Census | https://nationalarchives.ie | Search 1901, 1911 and 1926 census returns; surviving 19th-century fragments | Ireland | Free | None public † | None known | Earlier censuses largely destroyed in 1922 |
| RootsIreland | https://www.rootsireland.ie | Transcribed parish registers from county heritage centres | Ireland, all 32 counties | Paid | None public † | None known | Covers parishes not on IrishGenealogy.ie |
| PRONI | https://www.nidirect.gov.uk/proni | Wills calendars, valuation records, church record guides | Northern Ireland | Free | None public † | None known | |
| British Newspaper Archive | https://www.britishnewspaperarchive.co.uk | Full-text search of digitised newspapers | UK and Ireland | Paid; some free pages | None public † | None known | Same content available through Findmypast |
| Welsh Newspapers Online | https://newspapers.library.wales | Full-text newspaper search to 1919 | Wales | Free | Not confirmed | None known | National Library of Wales |
| TheGenealogist | https://www.thegenealogist.co.uk | Census, tithe maps, non-conformist records, map-based search | England and Wales | Paid | None public † | None known | Strong for tithe and land records |
| GENUKI | https://www.genuki.org.uk | Reference pages per county and parish: what records exist and where | UK and Ireland | Free | None | None known | Finding aid, not a record database |
| Commonwealth War Graves Commission | https://www.cwgc.org | Search war dead of both world wars; cemetery details | Commonwealth forces worldwide | Free | None public †; search results downloadable as CSV | None known | |
| FIBIS | https://www.fibis.org | Indexes of births, marriages, deaths and service records | British India and South Asia | Free search; membership for extras | None public † | None known | Families in British India Society |

## 4. Germany, Austria and central Europe

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| Archion | https://www.archion.de | Browse digitised Protestant church books | Germany (Protestant state churches) | Paid; catalogue of holdings free | None public † | None known | Images are not name-indexed in general; browse by parish and year |
| Matricula | https://data.matricula-online.eu | Browse digitised church books | Mainly Catholic: Germany, Austria, Poland, Luxembourg, Slovenia, others | Free | None public † | None known | No name index; pair with FamilySearch indexes to locate entries |
| CompGen (genealogy.net) | https://www.compgen.de | GenWiki, address books, local family books (OFB), GEDBAS tree database, gravestone project, casualty lists | German-speaking regions and former German territories | Free | GOV gazetteer has a web service and linked open data; others not confirmed | None known | Verein für Computergenealogie.  Mailing lists and forum at https://discourse.genealogy.net |
| GOV - Historic Gazetteer | https://gov.genealogy.net | Identify places, parishes and changing administrative hierarchy over time | Central Europe and beyond | Free | Yes, web service and linked open data | None known | Each place has a stable GOV identifier usable in tree software |
| Meyers Gazetteer | https://www.meyersgaz.org | Look up any place in the German Empire of 1871-1918 with parish and civil registry office | German Empire | Free | None public † | None known | Use the www. prefix |
| Deutsche Digitale Bibliothek, Archivportal-D, Deutsches Zeitungsportal | https://www.deutsche-digitale-bibliothek.de | Search holdings of German archives; full-text search of historical newspapers 1671-1950 | Germany | Free | Yes, REST API with OpenAPI spec; free key via user account | None known | Metadata released under CC0.  Covers Bundesarchiv data delivered to Archivportal-D |
| Arcinsys | https://www.arcinsys.de | Search and order from state archives | Lower Saxony, Hesse, Schleswig-Holstein, Bremen | Free | None public † | None known | Finding aid; many civil registers and church book duplicates digitised |
| Bundesarchiv - invenio | https://invenio.bundesarchiv.de | Search federal archive holdings, including military and Reich-era personnel files | Germany | Free | Data available through the DDB API and Archives Portal Europe API | None known | |
| Arolsen Archives | https://collections.arolsen-archives.org | Search documents on victims of Nazi persecution, forced labourers and displaced persons | Europe-wide | Free | No official API | Community (wspringer/arolsen-mcp, built on undocumented endpoints) | Terms prohibit integrating digitised material into other archives without consent |
| Volksbund Gräbersuche | https://www.volksbund.de | Search German war dead and missing of both world wars | German forces, graves worldwide | Free | None public † | None known | |
| Ahnenforschung.net forum | https://forum.ahnenforschung.net | Community help, including reading old handwriting | German-speaking regions | Free | None | None known | |
| GenTeam | https://www.genteam.at | Volunteer indexes to parish registers, obituaries, gazetteer | Austria and former Habsburg lands | Free (registration) | None public † | None known | |
| ANNO | https://anno.onb.ac.at | Full-text search of historical newspapers and periodicals | Austria and Habsburg lands | Free | Not confirmed | None known | Austrian National Library |
| Porta fontium | https://www.portafontium.eu | Browse parish registers | Western Bohemia and Bavarian border area | Free | None public † | None known | Other Czech regional archives run similar sites (e.g. Acta Publica for Moravia) |
| Geneteka | https://geneteka.genealodzy.pl | Volunteer index to parish and civil registers, with links to scans | Poland and former Polish territories | Free | None public † | None known | Polish Genealogical Society |
| Szukaj w Archiwach | https://www.szukajwarchiwach.gov.pl | Browse scans from Polish state archives | Poland, including former Prussian provinces | Free | None public † | None known | Holds many records for former German areas east of the Oder |
| BaSIA | https://www.basia.famula.pl | Index to archival records | Greater Poland (Posen) | Free | None public † | None known | |

## 5. Rest of Europe

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| Open Archives (Open Archieven) | https://www.openarchieven.nl | One search across indexed records of many archives; links to scans | Netherlands, Belgium | Free; subscription for extras | Yes, free, 4 requests per second; a few methods need a key.  OAI-PMH harvesting also available | Yes (coret/openarchieven-mcp-server, published by the operator, 21 tools) | Interface in NL, EN, DE, FR |
| WieWasWie | https://www.wiewaswie.nl | Search civil registration and population registers | Netherlands | Freemium | None public † | None known | Advanced search needs subscription |
| Delpher | https://www.delpher.nl | Full-text search of newspapers, books, periodicals | Netherlands and former colonies | Free | Data services via the National Library †; not rechecked | None known | |
| State Archives of Belgium | https://agatha.arch.be | Search and browse parish and civil registers | Belgium | Free (registration) | None public † | None known | |
| Filae | https://www.filae.com | Indexed civil registration and census records | France | Paid | None public † | None known | Owned by MyHeritage |
| French departmental archives | https://francearchives.gouv.fr | Browse parish and civil registers, censuses, military registers on each département's own site | France | Free | Varies by département | None known | FranceArchives is the national finding aid with links to each département |
| Gallica | https://gallica.bnf.fr | Full-text search of books, newspapers, directories | France | Free | Yes, open APIs † | Community (included in some multi-archive servers) | Bibliothèque nationale de France |
| Mémoire des hommes | https://www.memoiredeshommes.sga.defense.gouv.fr | Search military dead and service records | France | Free | None public † | None known | |
| Antenati | https://antenati.cultura.gov.it | Browse and partly search civil registration from state archives | Italy | Free | Images served by IIIF †; no search API | None known | Coverage varies by province |
| PARES | https://pares.cultura.gob.es | Search Spanish state archives, including emigration to the Americas | Spain and former colonies | Free | None public † | None known | Church records mostly remain with dioceses or on FamilySearch |
| Tombo.pt | https://tombo.pt | Directory linking to digitised parish registers in district archives | Portugal | Free | None | None known | |
| Riksarkivet (Sweden) | https://riksarkivet.se | Browse church records, censuses, court and military records; searchable databases | Sweden | Free | Yes, open, no key.  Returns data older than 110 years | Official, beta (ra-mcp, hosted at https://riksarkivet-ra-mcp.hf.space/mcp).  Beta runs September to end of 2026 | AI-transcribed text currently covers mainly court records.  Church records are digitised but not AI-transcribed |
| ArkivDigital | https://www.arkivdigital.net | Colour images of church and other records with name indexes | Sweden | Paid | None public † | None known | Often easier to navigate than the free Riksarkivet images |
| Digitalarkivet | https://www.digitalarkivet.no | Search transcribed censuses and church books; browse scans | Norway | Free | Not confirmed | None known | National Archives of Norway |
| Rigsarkivet / Danish Family Search | https://www.rigsarkivet.dk and https://www.danishfamilysearch.dk | Browse church books and censuses; search transcribed censuses | Denmark | Free | Not confirmed | None known | |
| HisKi | https://hiski.genealogia.fi | Search transcribed parish registers | Finland | Free | None public † | None known | Genealogical Society of Finland |
| Saaga | https://www.ra.ee/dgs | Browse church and census records | Estonia | Free (registration) | None public † | None known | |
| Raduraksti | https://raduraksti.arhivi.lv | Browse church books and revision lists | Latvia | Free (registration) | None public † | None known | |
| Pamyat Naroda | https://pamyat-naroda.ru | Search Second World War service and loss records | Former Soviet Union | Free | None public † | None known | Access from outside Russia can be unreliable |
| Archives Portal Europe | https://www.archivesportaleurope.net | Search finding aids of archives across Europe | Europe-wide | Free | Yes, with key † | None known | |
| Europeana | https://www.europeana.eu | Search digitised cultural heritage: photos, newspapers, documents | Europe-wide | Free | Yes, free key with a Europeana account | Community (pipeworx-io/mcp-europeana; also in multi-archive servers) | |

## 6. North America

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| NARA - National Archives Catalog | https://catalog.archives.gov | Search federal records: census, military, immigration, naturalisation | United States | Free | Yes, free key requested by email | Community (GeorgeTownSabatical/nara-mcp-server; Arke Institute semantic search) | |
| Chronicling America | https://chroniclingamerica.loc.gov | Full-text search of historic newspapers | United States | Free | Yes, through the loc.gov API, no key | Community (Library of Congress MCP servers exist) | The separate legacy API was retired in 2025 |
| Fold3 | https://www.fold3.com | Military service and pension records | United States; some UK and Commonwealth | Paid | None public † | None known | Owned by Ancestry |
| Newspapers.com | https://www.newspapers.com | Full-text newspaper search and clipping | United States; some UK, Canada, Australia | Paid | None public † | None known | Owned by Ancestry |
| GenealogyBank | https://www.genealogybank.com | Newspapers and obituaries | United States | Paid | None public † | None known | |
| American Ancestors | https://www.americanancestors.org | Databases and journals, strong on colonial families | United States, especially New England | Paid; some free | None public † | None known | New England Historic Genealogical Society |
| Statue of Liberty - Ellis Island Foundation | https://heritage.statueofliberty.org | Search passenger arrivals at New York | United States immigration, 1820-1957 | Free (registration) | None public † | None known | Steve Morse's One-Step tools (https://stevemorse.org) give better search forms for the same data |
| USGenWeb | https://www.usgenweb.org | Volunteer county-level transcriptions and guides | United States | Free | None | None known | Quality varies by county |
| Library and Archives Canada | https://library-archives.canada.ca | Search censuses, immigration, military files | Canada | Free | Not confirmed | None known | |
| Généalogie Québec (Drouin) | https://www.genealogiequebec.com | Parish registers and the LAFRANCE index | Quebec and French Canada | Paid | None public † | None known | |
| Canadiana | https://www.canadiana.ca | Digitised books, periodicals, government documents | Canada | Free | Not confirmed | None known | |

## 7. Latin America and the Caribbean

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| FamilySearch collections | https://www.familysearch.org | Catholic parish registers and civil registration, largely as browsable images | All of Latin America | Free (registration) | See section 1 | See section 1 | The main online source for most countries in the region |
| SIAN - Arquivo Nacional | https://sian.an.gov.br | Search national archive holdings, including immigration | Brazil | Free (registration) | None public † | None known | |
| Slave Voyages | https://www.slavevoyages.org | Databases of transatlantic and intra-American slave voyages | Atlantic world | Free | Not confirmed | None known | |
| Enslaved.org | https://enslaved.org | Linked records of named enslaved people | Atlantic world | Free | Not confirmed | None known | |

## 8. Asia and the Pacific

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| Trove | https://trove.nla.gov.au | Full-text search of newspapers, gazettes, books, images | Australia | Free | Yes, version 3; free key required for ongoing use | Community (littlebearapps/australian-history-mcp, also covers Public Record Office Victoria and others) | Aggregates over 1,500 partner collections |
| National Archives of Australia - RecordSearch | https://recordsearch.naa.gov.au | Immigration, naturalisation and military service files | Australia | Free | None public † | None known | State registries hold birth, marriage and death indexes, each with its own site |
| Papers Past | https://paperspast.natlib.govt.nz | Full-text search of newspapers, magazines, letters | New Zealand | Free | Yes, through the DigitalNZ API (key; non-commercial use for Papers Past data) | Community (DigitalNZ included in some multi-archive servers) | |
| BDM Online | https://www.bdmhistoricalrecords.dia.govt.nz | Search historical birth, death and marriage indexes; order printouts | New Zealand | Index free; copies paid | None public † | None known | |
| My China Roots | https://www.mychinaroots.com | Clan genealogies (jiapu/zupu), village research services | China and overseas Chinese | Freemium / paid services | None public † | None known | FamilySearch also holds a large jiapu collection |
| Shanghai Library genealogy database | https://jiapu.library.sh.cn | Catalogue and images of Chinese clan genealogies | China | Free | Not confirmed | None known | Chinese-language interface |
| Koseki (family registers) | Municipal offices; no central site | Request certified copies from the municipality where the family was registered | Japan | Fee per copy | None | None | Not searchable online.  Access limited to direct descendants and their agents |
| Israel Genealogy Research Association | https://genealogy.org.il | Indexed databases from the Ottoman, Mandate and Israeli periods | Israel / Palestine | Freemium | None public † | None known | |

## 9. Africa

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| eGGSA | https://www.eggsa.org | Transcriptions, gravestone photos, newspaper extracts | South Africa | Free | None public † | None known | Online branch of the Genealogical Society of South Africa |
| National Archives of South Africa - NAAIRS | https://www.nationalarchives.gov.za | Index to estate files and other archival records | South Africa | Free | None public † | None known | Index only; documents ordered separately or found on FamilySearch |
| FamilySearch oral genealogies | https://www.familysearch.org | Recorded oral lineages and civil and church records | Sub-Saharan Africa | Free (registration) | See section 1 | See section 1 | Online coverage elsewhere on the continent is thin; local archives and churches remain the main route |

## 10. Jewish and other specialist resources

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| JewishGen | https://www.jewishgen.org | Unified search of country databases, Family Finder, burial registry, Holocaust database | Worldwide Jewish communities, strongest for central and eastern Europe | Free (registration); donation unlocks advanced search | None public † | None known | Hosts over 1.5 million Arolsen Archives records |
| JRI-Poland | https://jri-poland.org | Index to Jewish vital records | Poland | Free (registration) | None public † | None known | |
| Yad Vashem - Names Database | https://yvng.yadvashem.org | Search Shoah victims and Pages of Testimony | Europe | Free | None public † | None known | |
| USHMM Survivors and Victims Database | https://www.ushmm.org | Search name lists from many sources | Europe | Free | None public † | None known | |
| Guild of One-Name Studies | https://one-name.org | Find researchers studying a single surname worldwide | Worldwide | Free search; membership | None | None known | |

## 11. Books, newspapers and maps (general)

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| Internet Archive | https://archive.org | Digitised local histories, directories, published genealogies | Worldwide | Free | Yes, open † | Community (several, including hosted versions) | |
| Google Books | https://books.google.com | Full-text search of out-of-copyright books | Worldwide | Free | Yes, key from Google Cloud | Community (in multi-archive servers) | |
| HathiTrust | https://www.hathitrust.org | Full-text search of digitised library books | Worldwide; full view varies by country | Free | Yes † | None known | |
| Old Maps Online | https://www.oldmapsonline.org | Find historical maps by location | Worldwide | Free | Not confirmed | None known | |
| GeoNames | https://www.geonames.org | Standardise place names and coordinates | Worldwide | Free | Yes, free account † | None known | Modern places; use GOV or Meyers for historical jurisdictions |

## 12. Software, standards and reference

| Resource | Website | How it is used | Country focus | Cost | API | MCP | Notes |
|---|---|---|---|---|---|---|---|
| Gramps / Gramps Web | https://gramps-project.org and https://www.grampsweb.org | Open-source desktop tree software; Gramps Web is the self-hosted or hosted web version | n/a | Free | Gramps Web has a REST API | Community, several (cabout-me/gramps-mcp, Genealogy-MCP/gramps-mcp, dsblank/gramps-ez-mcp, nikkoxgonzales/mcp-grampsweb) | Best-served tree software for MCP.  Data stays under your control |
| webtrees | https://webtrees.net | Open-source self-hosted web tree, GEDCOM-based | n/a | Free | None official † | None known | |
| RootsMagic, Family Tree Maker, Legacy | https://www.rootsmagic.com, https://www.mackiev.com, https://legacyfamilytree.com | Desktop tree software; RootsMagic and Family Tree Maker sync with Ancestry; RootsMagic and Legacy work with FamilySearch | n/a | Paid; free editions for some | None | None known | The supported way to move data in and out of Ancestry trees |
| GEDCOM | https://gedcom.io | Standard file format for exchanging tree data between programs and sites | n/a | Free | n/a | Community, several for local .ged files (airy10/GedcomMCP, sjmatta/gedcom-mcp, Genealogy-MCP/gedcom-mcp) | Export from any platform, then query locally.  reeeeemo/ancestry-mcp is archived |
| Transkribus | https://www.transkribus.org | Handwritten text recognition for old scripts, including Kurrent and Sütterlin | n/a | Freemium | Yes † | None known | |
| FamilySearch Research Wiki | https://www.familysearch.org/en/wiki/ | Country and locality guides: which records exist, where, and how to read them | Worldwide | Free | None | None known | Good starting point for any unfamiliar country |
| Cyndi's List | https://www.cyndislist.com | Categorised directory of genealogy links | Worldwide | Free | None | None known | |

---

## Practical notes

- **Start free, then pay selectively.**  FamilySearch, the national archive sites and the volunteer indexes cover a large share of what the subscription sites sell.  Subscription sites add indexing, hints and convenience.
- **Indexes versus images.**  Many free church-record sites (Matricula, Archion, Antenati, Riksarkivet) are image browsers without name search.  Use an index elsewhere to find the parish and date, then verify against the image.
- **Automation.**  Where no API exists, automated collection usually breaches the terms of use (Find a Grave and Arolsen Archives state this).  The supported route for your own data on closed platforms is GEDCOM export.
- **Privacy cut-offs.**  Most countries restrict records of living people: roughly 100 years for births, 70-80 for marriages, 25-50 for deaths, with national variation.  Riksarkivet's API, for example, returns only data older than 110 years.
- **Community MCP servers.**  Maintenance is uneven and repositories come and go; one FamilySearch server reviewed earlier in 2026 has since been deleted.  Check the repository activity before depending on one.

## Sources checked on 10 November 2026

- FamilySearch developer site: https://developers.familysearch.org
- FamilySearch third-party provider application: https://www.familysearch.org/innovate/apply
- ChatForest review of genealogy MCP servers: https://chatforest.com/reviews/genealogy-family-history-mcp-servers/
- JonathanReiss15/familysearch-mcp: https://github.com/JonathanReiss15/familysearch-mcp
- WikiTree API documentation: https://github.com/wikitree/wikitree-api
- Gramps Web MCP release thread: https://gramps.discourse.group/t/release-gramps-web-mcp-v1-0-connect-ai-assistants-to-your-family-tree/8541
- Open Archives API: https://www.openarchieven.nl/api/docs
- Open Archives MCP server: https://mcpservers.org/servers/coret/openarchieven-mcp-server
- Riksarkivet MCP beta: https://riksarkivet.se/en/resources/beta-test-our-new-ai-chat
- Riksarkivet API terms: https://sok.riksarkivet.se/en/data-api/api/anvandarvillkor-api
- The National Archives Discovery API: https://www.nationalarchives.gov.uk/help/discovery-for-developers-about-the-application-programming-interface-api/
- MCPBundles National Archives Discovery server: https://www.mcpbundles.com/skills/national-archives
- NARA Catalog API: https://www.archives.gov/research/catalog/help/api
- Chronicling America API: https://www.loc.gov/apis/additional-apis/chronicling-america-api/
- Trove API: https://trove.nla.gov.au/about/create-something/using-api
- Australian History MCP server: https://www.pkgstats.com/pkg:@littlebearapps/australian-history-mcp
- Papers Past via DigitalNZ: https://natlib.govt.nz/blog/tagged/166/open-data/papers-past-metadata/papers-past-data-using-the-digitalnz-api
- Deutsche Digitale Bibliothek newspaper portal FAQ: https://www.deutsche-digitale-bibliothek.de/content/newspaper/fragen-antworten
- Bundesarchiv open data: https://www.bundesarchiv.de/das-bundesarchiv/aus-unserer-archivarbeit/open-data/
- CompGen GOV: https://gov.genealogy.net
- Arolsen MCP server (unofficial): https://github.com/wspringer/arolsen-mcp
- Europeana APIs: https://www.europeana.eu/en/apis
- Wikidata MCP server: https://glama.ai/mcp/connectors/io.github.cyanheads/wikidata-mcp-server
