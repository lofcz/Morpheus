"""
Synthesize unique organization type terms for org_types.txt
Uses common Czech patterns and combinations to generate realistic business type terms.
"""
import os
import random

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ORG_TYPES_PATH = os.path.join(SCRIPT_DIR, "data", "classes", "sources", "org_types.txt")

# Base components for Czech business terms
PREFIXES = [
    # General/Tech
    "auto", "česko", "euro", "moto", "photo", "foto", "video", "audio", "techno",
    "bio", "eco", "elektro", "krypto", "cyber", "digi", "smart", "web",
    "gastro", "agro", "hydro", "termo", "metro", "retro", "mikro", "makro",
    "mono", "poly", "multi", "trans", "inter", "super", "hyper", "mega", "mini",
    "neo", "novo", "classic", "royal", "grand", "luxus", "premium", "elite",
    "express", "quick", "rapid", "fast", "speed", "power", "max", "plus",
    "pro", "expert", "master", "top", "best", "first", "main", "central",
    
    # Industry-specific
    "aero", "aqua", "astro", "cardio", "chemo", "chrono", "cosmo", "crypto",
    "demo", "derma", "digi", "eco", "ergo", "ethno", "geo", "gero", "gyno",
    "hemo", "hetero", "hipo", "homo", "hydro", "immuno", "info", "iso", "kilo",
    "logo", "macro", "media", "medico", "metro", "neuro", "onco", "ortho", "osteo",
    "para", "patho", "pharma", "physio", "pneumo", "psycho", "radio", "rhino",
    "socio", "stereo", "tele", "thermo", "toxi", "ultra", "viro", "zoo",
    
    # Czech prefixes
    "český", "české", "moravský", "slezský", "pražský", "brněnský", "národní",
    "státní", "městský", "krajský", "okresní", "regionální", "místní", "obecní",
    "komunitní", "rodinný", "soukromý", "veřejný", "nezávislý", "svobodný",
]

CORES = [
    # Services & Business
    "služby", "centrum", "servis", "studio", "salon", "klub", "shop", "market",
    "store", "obchod", "bazár", "galerie", "design", "lab", "factory", "works",
    "group", "trade", "consulting", "management", "development", "solutions",
    "systems", "technologies", "innovations", "ventures", "partners", "associates",
    "advisors", "professionals", "experts", "specialists", "consulting",
    "poradenství", "řešení", "technologie", "systémy", "inovace", "projekty",
    
    # Production & Logistics
    "výroba", "produkce", "distribuce", "logistika", "transport", "doprava",
    "manufacturing", "assembly", "packaging", "warehousing", "shipping", "delivery",
    "zásobování", "expedice", "skladování", "balení", "komise", "výdej",
    
    # Construction & Trades
    "stavby", "montáže", "instalace", "revize", "opravy", "údržba",
    "návrhy", "realizace", "dodávky", "prodej", "pronájem", "leasing",
    "výstavba", "rekonstrukce", "renovace", "sanace", "demolice", "projekce",
    "construction", "building", "engineering", "architecture", "design",
    
    # Real Estate & Property
    "finance", "investice", "pojištění", "reality", "nemovitosti", "domy",
    "byty", "pozemky", "stavby", "projekty", "developerské", "stavební",
    "estates", "properties", "housing", "residential", "commercial",
    
    # Creative & Workspace
    "ateliér", "dílna", "workshop", "space", "místo", "prostor", "arena",
    "studio", "workspace", "coworking", "maker", "creative", "innovation",
    
    # Medical & Healthcare
    "klinika", "ordinace", "ambulance", "poliklinika", "sanatorium", "lázeň",
    "clinic", "hospital", "medical", "health", "care", "wellness", "therapy",
    "diagnostika", "prevence", "screening", "vyšetření", "léčba", "péče",
    
    # Education & Training
    "škola", "akademie", "univerzita", "institut", "fakulta", "katedra",
    "school", "academy", "university", "college", "institute", "training",
    "vzdělávání", "výuka", "školení", "kurzy", "semináře", "příprava",
    
    # Food & Beverage
    "restaurace", "bistro", "kavárna", "cukrárna", "pekárna", "vinotéka",
    "restaurant", "cafe", "bar", "pub", "brewery", "bakery", "patisserie",
    "stravování", "catering", "gastronomie", "pohostinství", "hostinec",
    
    # Retail & Commerce
    "prodejna", "obchodní dům", "nákupní centrum", "tržiště", "shop", "store",
    "retail", "outlet", "boutique", "gallery", "showroom", "marketplace",
    "e-shop", "eshop", "online store", "webshop", "internetový obchod",
    
    # Technology & IT
    "software", "hardware", "aplikace", "systém", "platforma", "síť",
    "tech", "digital", "cloud", "data", "analytics", "ai", "blockchain",
    "development", "programming", "coding", "automation", "robotics",
    
    # Finance & Insurance
    "banka", "pojišťovna", "investice", "finance", "hypotéky", "úvěry",
    "banking", "insurance", "investment", "financial", "capital", "fund",
    "účetnictví", "audit", "daně", "poradenství", "consulting", "advisory",
    
    # Arts & Culture
    "divadlo", "kino", "muzeum", "galerie", "výstava", "festival",
    "theater", "cinema", "museum", "gallery", "exhibition", "cultural",
    "umění", "kultura", "scéna", "podium", "stage", "performance",
    
    # Sports & Recreation
    "sport", "fitness", "gym", "arena", "stadion", "hřiště", "hala",
    "athletic", "recreation", "leisure", "activity", "training", "coaching",
    "tělocvična", "posilovna", "trénink", "cvičení", "sport", "atletika",
    
    # Beauty & Personal Care
    "kosmetika", "kadeřnictví", "holičství", "manikúra", "pedikúra", "spa",
    "beauty", "cosmetic", "hair", "nails", "massage", "wellness", "salon",
    "péče", "krása", "ošetření", "terapie", "relaxace", "masáže",
    
    # Automotive
    "auto", "motorky", "vozidla", "doprava", "mechanika", "lakování",
    "automotive", "cars", "vehicles", "motorcycle", "repair", "service",
    "diagnostika", "servis", "opravy", "údržba", "tuning", "customizace",
    
    # Agriculture & Environment
    "zahrada", "farma", "zemědělství", "lesnictví", "ekologie", "recyklace",
    "agriculture", "farming", "gardening", "forestry", "ecology", "recycling",
    "pěstování", "chov", "hospodářství", "plantáž", "sklizeň", "úroda",
    
    # Legal & Administration
    "právní", "advokátní", "notářská", "arbitráž", "mediace", "zprostředkování",
    "legal", "law", "attorney", "notary", "arbitration", "mediation",
    "zastupování", "obhajoba", "poradenství", "řízení", "administrativa",
    
    # Media & Communication
    "média", "redakce", "vydavatelství", "tiskárna", "reklama", "marketing",
    "media", "publishing", "printing", "advertising", "marketing", "PR",
    "komunikace", "propagace", "branding", "kreativa", "obsahy", "social",
    
    # Tourism & Hospitality
    "hotel", "penzion", "ubytování", "cestování", "turistika", "průvodce",
    "tourism", "travel", "hospitality", "accommodation", "hotel", "resort",
    "cestovní", "zájezdy", "výlety", "dovolená", "rekreace", "poznávání",
    
    # Energy & Utilities
    "energie", "elektřina", "plyn", "voda", "topení", "klimatizace",
    "energy", "power", "electricity", "gas", "water", "heating", "cooling",
    "zásobování", "rozvod", "distribuce", "sítě", "instalace", "měření",
    
    # Security & Safety
    "bezpečnost", "охrana", "охрана", "security", "protection", "surveillance",
    "охранные", "alarm", "monitoring", "охранительные", "protection", "safety",
    "охраните", "strážní", "ostraha", "dozor", "monitorování", "kontrola",
    
    # Cleaning & Maintenance
    "úklid", "čištění", "mytí", "údržba", "facility", "cleaning", "maintenance",
    "úklidové", "čistící", "dezinfekce", "hygienické", "sanitace", "sanace",
]

SUFFIXES = [
    # Organizational
    "team", "crew", "squad", "band", "group", "club", "society", "union",
    "association", "federation", "alliance", "network", "chain", "collective",
    "cooperative", "enterprise", "corporation", "company", "firm", "agency",
    "bureau", "office", "institute", "academy", "school", "center", "hub",
    "consortium", "syndicate", "cartel", "trust", "holding", "conglomerate",
    
    # Location-based
    "spot", "zone", "plaza", "mall", "park", "world", "land", "city",
    "house", "home", "base", "station", "point", "corner", "place",
    "arena", "venue", "facility", "complex", "compound", "premises",
    "campus", "site", "location", "destination", "quarter", "district",
    
    # Service-oriented
    "service", "services", "solutions", "systems", "works", "lab", "labs",
    "workshop", "studio", "atelier", "depot", "warehouse", "storage",
    "outlet", "showroom", "gallery", "exhibition", "expo", "fair",
    
    # Czech organizational
    "spolek", "sdružení", "družstvo", "uskupení", "organizace", "společnost",
    "jednota", "svaz", "liga", "rada", "výbor", "komise", "sekce",
    "odbor", "oddělení", "divize", "jednotka", "úsek", "středisko",
    
    # Professional
    "professionals", "experts", "specialists", "consultants", "advisors",
    "practitioners", "technicians", "engineers", "architects", "designers",
    "developers", "analysts", "strategists", "planners", "coordinators",
    
    # Scale indicators
    "international", "global", "worldwide", "national", "regional", "local",
    "community", "neighborhood", "municipal", "provincial", "federal",
]

# Standalone Czech terms
STANDALONE_CZECH = [
    # Traditional Trades
    "zámečnictví", "klempířství", "pokrývačství", "tesařství", "truhlářství",
    "čalounění", "tapetování", "malířství", "lakýrnictví", "natěračství",
    "sklenářství", "rámování", "knihařství", "vazačství", "tiskařství",
    "kovodělství", "kovářství", "podkovářství", "bednářství", "hrnčířství",
    "kamnářství", "štukatérství", "sochařství", "kamenictví", "řezbářství",
    
    # Printing & Graphics
    "polygrafie", "kopírovačka", "copycentrum", "kopírovna", "copyshop",
    "tiskárna", "digitální tisk", "offsetový tisk", "sítotisk", "velkoformátový tisk",
    "grafické studio", "prepress", "dtp studio", "vazba dokumentů",
    
    # Photography & Video
    "fotografování", "fotoateliér", "fotolaboratoř", "fotoservis",
    "videoprodukce", "filmování", "střih", "postprodukce", "dabink",
    "nahrávání", "zvukařství", "nahrávací studio", "mix", "mastering",
    "videostudio", "filmová produkce", "kameramani", "osvětlovači",
    
    # Retail & Sales
    "prodejní místo", "výdejna", "výdejní místo", "sklad", "sklady",
    "pobočka", "zastoupení", "reprezentace", "expozice", "showroom",
    "vzorkovna", "předváděcí místnost", "demo centrum", "zkušebna",
    "prodejní galerie", "outlet", "second hand", "vintage shop", "bazar",
    
    # Rental Services
    "půjčovna", "rental", "autopůjčovna", "rent", "lease", "pronájem",
    "půjčovna nářadí", "půjčovna oblečení", "půjčovna knih", "videotéka",
    "půjčovna sportovního vybavení", "půjčovna lodí", "půjčovna kol",
    
    # Food Retail
    "trafika", "tabák", "tabačka", "novinový stánek", "stánek",
    "bufet", "bistro", "občerstvení", "rychlé občerstvení", "fastfood",
    "jídelna", "kantýna", "menza", "stravování", "catering",
    "cukrářství", "pekařství", "pečivo", "lahůdky", "lahůdkářství",
    "řeznictví", "uzenářství", "masna", "maso", "drůbež",
    "zelenina", "ovoce", "ovoce zelenina", "potraviny", "smíšené zboží",
    "rybárna", "rybí speciality", "mořské plody", "delikatesy",
    "cheese shop", "sýrárna", "vinný sklep", "pivotéka", "čajovna",
    
    # Personal Care & Beauty
    "drogerie", "chemické výrobky", "kosmetika", "parfumerie",
    "kadeřnictví", "holičství", "barbershop", "hair salon", "vlasový salon",
    "nehtové studio", "nehty", "manikúra", "pedikúra", "kosmetický salon",
    "masážní salon", "masáže", "wellness", "spa", "sauna", "bazén",
    "solárium", "tattoo studio", "piercing", "permanent makeup",
    "beauty bar", "lash bar", "brow bar", "make-up studio",
    
    # Healthcare
    "lékárna", "zdravotnické potřeby", "ortopedické pomůcky", "rehabilitační pomůcky",
    "ordinace", "ambulance", "poradna", "centrum", "pracoviště",
    "veterinární ambulance", "veterinární ordinace", "zvěrolékařství",
    "pohotovost", "emergency", "first aid", "sanitka", "záchranná služba",
    "domácí péče", "hospicová péče", "ošetřovatelská služba",
    "diagnostické centrum", "laboratoř", "rentgen", "ct vyšetření", "mri",
    
    # Fitness & Sports
    "fitness", "posilovna", "gym", "tělocvična", "sportcentrum", "sportovní klub",
    "crossfit box", "funkční trénink", "pilates studio", "yoga centrum",
    "spinning studio", "aerobic", "zumba", "dance fitness", "body combat",
    "box klub", "karate dojo", "judo klub", "taekwondo", "mma gym",
    "lezecká stěna", "boulder", "skatpark", "parkour", "freerunning",
    
    # Education & Arts
    "taneční studio", "taneční škola", "tanec", "dance studio", "baletní škola",
    "hudební škola", "hudebna", "music school", "konzervatoř", "hudební akademie",
    "jazyková škola", "jazykové kurzy", "jazykové centrum", "výuka jazyků",
    "výtvarný ateliér", "keramická dílna", "batikování", "šití", "patchwork",
    "dramatická škola", "herecký kurz", "improvizace", "stand-up workshop",
    
    # Driving & Automotive
    "autoškola", "řidičák", "řidičské oprávnění", "autoškolicí středisko",
    "autoprodej", "autosalon", "autobazar", "bazár aut", "ojetá vozidla",
    "autoopravna", "autoservis", "autodílna", "auto dílna", "garáž",
    "pneuservis", "pneumatiky", "pneu", "gumy", "výměna pneumatik",
    "lakovna", "autolakovna", "karosárna", "klempírna", "autoklempírna",
    "mycí linka", "mycí centrum", "autoumývárna", "čištění vozidel", "ruční mytí",
    "čerpací stanice", "benzinka", "benzínová pumpa", "tank station", "tankování",
    "autoelektrikář", "autodiagnostika", "emisní kontrola", "stk", "měření emisí",
    "autopůjčovna luxusních aut", "car sharing", "car rental", "rent a car",
    
    # Home & Garden
    "zahradnictví", "květinářství", "floristika", "zahradní architektura",
    "stromkárna", "arboristika", "péče o stromy", "sekání trávy", "zahradní úpravy",
    "okrasné rostliny", "bazény a jezírka", "závlahy", "automatické závlahy",
    "nábytek na zahradu", "venkovní dekorace", "grilovací technika",
    
    # Pets & Animals
    "chovatelské potřeby", "krmiva pro zvířata", "akvaristika", "teraristika",
    "psí salon", "kočičí hotel", "psí hotel", "venčení psů", "výcvik psů",
    "zverimex", "zoo obchod", "pet shop", "veterinární lékárna",
    
    # Technology & Electronics
    "servisní centrum", "it podpora", "počítačový servis", "opravy notebooků",
    "mobilní servis", "opravy telefonů", "výměna displejů", "odblokování",
    "opravy tabletů", "game shop", "herní klub", "pc café", "internet café",
    
    # Specialized Services
    "čistírna", "prádelna", "mandl", "žehlírna", "химчистка",
    "opravna obuvi", "key service", "zámečnická pohotovost", "výroba klíčů",
    "hodinářství", "opravy hodinek", "zlatnictví", "klenotnictví", "zastavárna",
    "notářská kancelář", "exekutorský úřad", "insolvence", "konkurzy",
    "tlumočnické služby", "překladatelská agentura", "sworn translator",
]

# Domain-specific Czech terms - much expanded
DOMAIN_SPECIFIC = [
    # IT/Tech/Software
    "it služby", "it podpora", "it řešení", "webdesign", "grafický design",
    "programování", "vývoj software", "vývoj aplikací", "mobilní aplikace",
    "cloudové služby", "hosting", "servery", "datové centrum", "data storage",
    "kybernetická bezpečnost", "penetrační testování", "ethical hacking",
    "devops", "continuous integration", "ci/cd", "kubernetes", "docker",
    "machine learning", "umělá inteligence", "deep learning", "neural networks",
    "blockchain development", "smart contracts", "kryptografie", "šifrování",
    "big data analytics", "business intelligence", "data science", "statistika",
    "erp systémy", "crm systémy", "e-commerce platformy", "payment gateway",
    "api development", "microservices", "serverless", "edge computing",
    "iot řešení", "smart home", "průmyslová automatizace", "scada systémy",
    "virtuální realita", "vr development", "ar aplikace", "3d modeling",
    "game development", "unity", "unreal engine", "herní design",
    "ui/ux design", "user experience", "prototyping", "wireframing",
    "seo optimalizace", "sem marketing", "performance marketing", "affiliate",
    "content management", "wordpress", "drupal", "joomla", "shopify",
    
    # Legal/Admin/Consulting
    "právní služby", "advokátní kancelář", "právní poradna", "notářství",
    "účetní služby", "účetnictví", "daňové poradenství", "daně", "audit",
    "corporate law", "commercial law", "ip právo", "patenty", "ochranné známky",
    "pracovní právo", "employment law", "hr consulting", "personalistika",
    "strategické poradenství", "management consulting", "change management",
    "compliance", "gdpr poradenství", "data protection", "ochrana údajů",
    "mediace", "arbitráž", "rozhodčí řízení", "adr", "mimosoudní řešení",
    "insolvence", "konkurzy", "restrukturalizace", "oddlužení", "exekuce",
    "due diligence", "finanční analýza", "oceňování společností", "valuace",
    "forenzní účetnictví", "soudní znalectví", "expert witness",
    
    # Real Estate & Construction
    "reality", "realitní kancelář", "realitní služby", "nemovitosti",
    "prodej nemovitostí", "pronájem bytů", "správa nemovitostí",
    "property management", "facility management", "správa budov",
    "developerské projekty", "rezidenční výstavba", "komerční výstavba",
    "projektová příprava", "územní plánování", "stavební povolení",
    "architektonické návrhy", "interiérový design", "zahradní architektura",
    "energetická certifikace", "penb", "průkaz energetické náročnosti",
    "stavební dozor", "technický dozor investora", "autorský dozor",
    "geodetické práce", "zaměření", "vytyčení", "pasportizace",
    "statické posudky", "konstrukční řešení", "betonové konstrukce",
    
    # Medical/Health/Pharma
    "lékařská ordinace", "zubní ordinace", "oční ordinace", "dětská ordinace",
    "gynekologická ordinace", "psychiatrická ordinace", "neurologická ordinace",
    "rehabilitační centrum", "fyzioterapie", "logopedická poradna",
    "kardiologická ambulance", "diabetologická poradna", "endokrinologie",
    "onkologické centrum", "radioterapie", "chemoterapie", "imunoterapie",
    "plastická chirurgie", "estetická medicína", "dermatologie", "alergologie",
    "ortopedická ambulance", "traumatologie", "sportovní medicína",
    "neurochirurgie", "kardiochirurgie", "cévní chirurgie", "hrudní chirurgie",
    "anesteziologie", "intenzivní péče", "jednotka intenzivní péče", "jip",
    "nukleární medicína", "pet/ct", "mamografie", "sonografie", "usg",
    "klinická laboratoř", "molekulární diagnostika", "genetické testování",
    "krevní testy", "mikrobiologie", "virologie", "imunologie",
    "farmakologie", "lékárenská péče", "klinické studie", "clinical trials",
    
    # Education & Training
    "vzdělávací centrum", "školicí středisko", "kurzy", "výukové centrum",
    "studovna", "čítárna", "knihovna", "mediální knihovna",
    "mateřská škola", "jesle", "školní družina", "základní škola",
    "střední škola", "gymnázium", "odborná škola", "učiliště",
    "vysoká škola", "univerzita", "fakulta", "institut", "akademie",
    "distance learning", "e-learning", "online kurzy", "webináře",
    "profesní vzdělávání", "rekvalifikace", "celoživotní vzdělávání",
    "montessori škola", "waldorfská škola", "alternativní vzdělávání",
    "speciální pedagogika", "logopedická péče", "dyslexie centrum",
    "doučování", "příprava na zkoušky", "maturity", "přijímačky",
    
    # Finance & Banking
    "finanční poradenství", "investiční poradenství", "hypotéky", "úvěry",
    "pojišťovna", "pojištění", "směnárna", "výměna měn", "exchange office",
    "private banking", "wealth management", "asset management", "portfolio",
    "penzijní fondy", "důchodové spoření", "investiční fondy", "etf",
    "retail banking", "korporátní finance", "project finance", "syndikace",
    "forex trading", "commodity trading", "akciové obchodování", "burza",
    "venture capital", "private equity", "startup akcelerátor", "inkubátor",
    "crowdfunding platforma", "peer-to-peer lending", "fintech",
    "kryptoměnová burza", "crypto exchange", "wallet", "blockchain",
    "factoring", "forfaiting", "leasing", "operativní leasing",
    
    # Entertainment & Leisure
    "zábavní centrum", "herní centrum", "bowling", "biliár", "kulečník",
    "squash", "golf", "minigolf", "adventure park", "zábavní park",
    "escape room", "úniková hra", "quest room", "mystery room",
    "laser tag", "laser game", "paintball", "airsoft", "tactical games",
    "trampolínové centrum", "jump park", "ninja park", "parkour hala",
    "vr arena", "virtuální realita", "simulátory", "racing simulátor",
    "casino", "herná", "automaty", "poker room", "sportovní sázení",
    "noční klub", "night club", "disco", "techno klub", "jazz club",
    "karaoke bar", "karaoke box", "karaoke salon", "zpěv",
    
    # Culture & Arts
    "kulturní centrum", "kulturní dům", "divadlo", "kino", "multikino",
    "muzeum", "galerie", "výstavní síň", "koncertní sál", "hudební klub",
    "filharmonie", "symfonický orchestr", "opera", "balet", "opereta",
    "repertoárové divadlo", "studiové divadlo", "alternativní divadlo",
    "loutkové divadlo", "černé divadlo", "pantomima", "cirkus",
    "planetárium", "hvězdárna", "observatoř", "science centrum",
    "umělecká galerie", "contemporary art", "moderní umění", "fotogalerie",
    "autorkino", "art-house cinema", "multiplex", "imax",
    "knihovna", "veřejná knihovna", "vědecká knihovna", "archiv",
    
    # Manufacturing & Industry
    "strojírenská výroba", "obrábění", "frézování", "soustružení", "vrtání",
    "kovoobráběčská dílna", "cnc obrábění", "5-osé frézování",
    "slévárna", "odlitky", "přesné lití", "tlakové lití", "gravitační lití",
    "kovovýroba", "svařování", "laserové svařování", "robot welding",
    "povrchové úpravy", "lakování", "galvanizace", "eloxování", "pozinkování",
    "lisování", "ražení", "ohýbání plechu", "laser cutting", "plasma cutting",
    "montážní linka", "assembly line", "výrobní hala", "průmyslová výroba",
    "automotive dodavatel", "tier 1", "tier 2", "oem výroba",
    "plastikářská výroba", "vstřikování", "extrůze", "vyfukování",
    "textilní výroba", "tkalcovna", "pletárna", "konfekce", "šití",
    "potravinářská výroba", "pekárna", "cukrárna", "masna", "mlékárna",
    
    # Logistics & Transport
    "spedice", "mezinárodní doprava", "vnitrostátní doprava", "kamionová doprava",
    "refrigerovaná doprava", "oversized transport", "nadrozměrný náklad",
    "kurýrní služby", "expresní doprava", "same-day delivery", "doručování",
    "skladování", "cross-docking", "fulfillment", "pick and pack",
    "logistické centrum", "distribution center", "sklad", "warehouse",
    "celní deklarace", "celní služby", "customs clearance", "import/export",
    "zasilatelství", "freight forwarding", "air freight", "sea freight",
    "kontejnerová doprava", "intermodální doprava", "rail freight",
    
    # Marketing & Advertising
    "marketingová agentura", "full-service agency", "creative agency",
    "digital marketing", "performance marketing", "growth hacking",
    "brand management", "branding", "corporate identity", "visual identity",
    "public relations", "pr agentura", "media relations", "crisis management",
    "event marketing", "event management", "corporate events", "konference",
    "influencer marketing", "social media marketing", "community management",
    "obsahový marketing", "content creation", "copywriting", "storytelling",
    "video marketing", "video produkce", "reklamní spoty", "commercial",
    "out-of-home", "ooh reklama", "billboards", "city light vitríny",
    "direct mail", "direct marketing", "telemarketing", "call centrum",
]

def load_existing():
    """Load existing org types from file."""
    if not os.path.exists(ORG_TYPES_PATH):
        return set()
    with open(ORG_TYPES_PATH, "r", encoding="utf-8") as f:
        return set(line.strip().lower() for line in f if line.strip())

def generate_synthetic():
    """Generate a synthetic org type term."""
    pattern = random.choice([
        "prefix_core",
        "core_suffix", 
        "prefix_core_suffix",
        "standalone",
        "domain",
    ])
    
    if pattern == "prefix_core":
        # 80% chance of space between prefix and core
        sep = " " if random.random() < 0.8 else ""
        return f"{random.choice(PREFIXES)}{sep}{random.choice(CORES)}"
    elif pattern == "core_suffix":
        # Always space between core and suffix
        return f"{random.choice(CORES)} {random.choice(SUFFIXES)}"
    elif pattern == "prefix_core_suffix":
        # 80% chance of space between prefix and core, always space before suffix
        sep = " " if random.random() < 0.8 else ""
        return f"{random.choice(PREFIXES)}{sep}{random.choice(CORES)} {random.choice(SUFFIXES)}"
    elif pattern == "standalone":
        return random.choice(STANDALONE_CZECH)
    else:  # domain
        return random.choice(DOMAIN_SPECIFIC)

def main():
    target_count = 20000
    existing = load_existing()
    initial_count = len(existing)
    
    print(f"Current org types count: {initial_count}")
    print(f"Target: {target_count} unique entries")
    print(f"Need to generate: {target_count - initial_count} new entries")
    print()
    
    if initial_count >= target_count:
        print("Already have enough entries!")
        return
    
    new_added = 0
    attempts = 0
    max_attempts = 50000  # Prevent infinite loop
    
    while len(existing) < target_count and attempts < max_attempts:
        attempts += 1
        candidate = generate_synthetic()
        candidate_lower = candidate.lower().strip()
        
        if candidate_lower and candidate_lower not in existing:
            existing.add(candidate_lower)
            new_added += 1
            
            if new_added % 100 == 0:
                print(f"Progress: {len(existing)}/{target_count} ({new_added} new entries added, {attempts} attempts)")
    
    # Sort alphabetically and write back
    sorted_entries = sorted(existing)
    
    with open(ORG_TYPES_PATH, "w", encoding="utf-8") as f:
        for entry in sorted_entries:
            f.write(f"{entry}\n")
    
    print()
    print(f"✓ Done! Added {new_added} new unique entries")
    print(f"✓ Total org types: {len(existing)}")
    print(f"✓ Saved to: {ORG_TYPES_PATH}")

if __name__ == "__main__":
    main()
