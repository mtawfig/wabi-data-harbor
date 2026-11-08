"""Generic, clearly fictional person names for the Wabi Bank sample, in English with an Arabic spelling.

Every person in the bank (employees, the eight Wabi Insight users and individual customers) is named from these lists.
They are deliberately neutral given names and uncommon English surnames, so no name reads as a real person, a public
figure or a particular family; nationality stays a separate attribute and does not change the name.

Each nationality's name group draws from its own slice of the lists, so the population stays varied. The slice sizes
(GROUP_SIZES) are fixed: the generators draw names from the shared random stream, and changing a slice size would
change every value drawn after it (dates, amounts, the planted findings described in README.md).
"""

MALE = [
    ("Adrian", "أدريان"), ("Alex", "أليكس"), ("Bruno", "برونو"), ("Calvin", "كالفن"), ("Chris", "كريس"), ("Colin", "كولن"),
    ("Daniel", "دانيال"), ("Dennis", "دينيس"), ("Dylan", "ديلان"), ("Edgar", "إدغار"), ("Edward", "إدوارد"), ("Elliot", "إليوت"),
    ("Eric", "إريك"), ("Felix", "فيليكس"), ("Frank", "فرانك"), ("Gavin", "غافن"), ("George", "جورج"), ("Gordon", "غوردن"),
    ("Graham", "غراهام"), ("Henry", "هنري"), ("Hugo", "هوغو"), ("Ian", "إيان"), ("Ivan", "إيفان"), ("Jack", "جاك"),
    ("Jason", "جيسون"), ("Jasper", "جاسبر"), ("Julian", "جوليان"), ("Kevin", "كيفن"), ("Lars", "لارس"), ("Leo", "ليو"),
    ("Liam", "ليام"), ("Lucas", "لوكاس"), ("Marcus", "ماركوس"), ("Martin", "مارتن"), ("Max", "ماكس"), ("Miles", "مايلز"),
    ("Nathan", "ناثان"), ("Neil", "نيل"), ("Nico", "نيكو"), ("Oliver", "أوليفر"), ("Oscar", "أوسكار"), ("Owen", "أوين"),
    ("Patrick", "باتريك"), ("Paul", "بول"), ("Peter", "بيتر"), ("Philip", "فيليب"), ("Quinn", "كوين"), ("Ralph", "رالف"),
    ("Robert", "روبرت"), ("Roger", "روجر"), ("Rufus", "روفوس"), ("Ryan", "رايان"), ("Sam", "سام"), ("Scott", "سكوت"),
    ("Sebastian", "سيباستيان"), ("Simon", "سايمون"), ("Stefan", "ستيفان"), ("Steven", "ستيفن"), ("Theo", "ثيو"), ("Thomas", "توماس"),
    ("Tobias", "توبياس"), ("Tom", "توم"), ("Victor", "فيكتور"), ("Vincent", "فنسنت"), ("Warren", "وارن"), ("Wesley", "ويسلي"),
    ("William", "ويليام"), ("Xavier", "زافيير"), ("Zack", "زاك"),
]

FEMALE = [
    ("Alice", "أليس"), ("Amber", "أمبر"), ("Amy", "إيمي"), ("Anna", "آنا"), ("Beth", "بيث"), ("Bella", "بيلا"), ("Carla", "كارلا"),
    ("Chloe", "كلوي"), ("Clara", "كلارا"), ("Daisy", "ديزي"), ("Elena", "إيلينا"), ("Ella", "إيلا"), ("Emily", "إيميلي"), ("Erin", "إيرين"),
    ("Eva", "إيفا"), ("Fiona", "فيونا"), ("Freya", "فريا"), ("Gemma", "جيما"), ("Grace", "غريس"), ("Hannah", "هانا"), ("Hazel", "هيزل"),
    ("Helen", "هيلين"), ("Holly", "هولي"), ("Iris", "آيريس"), ("Ivy", "آيفي"), ("Jane", "جين"), ("Julia", "جوليا"), ("Kate", "كيت"),
    ("Laura", "لورا"), ("Lily", "ليلي"), ("Lucy", "لوسي"), ("Megan", "ميغان"), ("Mia", "ميا"), ("Naomi", "ناومي"), ("Nina", "نينا"),
    ("Olivia", "أوليفيا"), ("Paula", "باولا"), ("Phoebe", "فيبي"), ("Rachel", "رايتشل"), ("Rose", "روز"), ("Ruby", "روبي"),
    ("Sophie", "صوفي"), ("Stella", "ستيلا"), ("Tessa", "تيسا"), ("Una", "أونا"), ("Vera", "فيرا"), ("Wendy", "ويندي"),
    ("Yvonne", "إيفون"), ("Zoe", "زوي"),
]

SURNAMES = [
    ("Ashby", "آشبي"), ("Ashton", "آشتون"), ("Bradwell", "برادويل"), ("Bexley", "بيكسلي"), ("Bramley", "براملي"), ("Calder", "كالدر"),
    ("Carver", "كارفر"), ("Corbin", "كوربن"), ("Cranmore", "كرانمور"), ("Dalton", "دالتون"), ("Darby", "داربي"), ("Dawes", "دوز"),
    ("Denton", "دنتون"), ("Easton", "إيستون"), ("Elmore", "إلمور"), ("Emberly", "إمبرلي"), ("Everly", "إيفرلي"), ("Fairley", "فيرلي"),
    ("Fenwick", "فينويك"), ("Finch", "فينش"), ("Garland", "غارلاند"), ("Harlow", "هارلو"), ("Hartwell", "هارتويل"), ("Hollis", "هوليس"),
    ("Ingram", "إنغرام"), ("Kendall", "كيندال"), ("Langley", "لانغلي"), ("Lindley", "ليندلي"), ("Linford", "لينفورد"), ("Lockwood", "لوكوود"),
    ("Marlow", "مارلو"), ("Mercer", "ميرسر"), ("Merritt", "ميريت"), ("Moreland", "مورلاند"), ("Newell", "نيويل"), ("Norwood", "نوروود"),
    ("Oakley", "أوكلي"), ("Orton", "أورتون"), ("Pembroke", "بيمبروك"), ("Penrose", "بنروز"), ("Prescott", "بريسكوت"), ("Radley", "رادلي"),
    ("Redfern", "ريدفيرن"), ("Rowley", "راولي"), ("Saxby", "ساكسبي"), ("Selby", "سيلبي"), ("Stanton", "ستانتون"), ("Sutton", "ساتون"),
    ("Tanner", "تانر"), ("Thornbury", "ثورنبري"), ("Tilney", "تيلني"), ("Varley", "فارلي"), ("Vickers", "فيكرز"), ("Wakefield", "ويكفيلد"),
    ("Weston", "ويستون"), ("Whitmore", "ويتمور"), ("Winslow", "وينسلو"), ("Yardley", "ياردلي"), ("Abbott", "آبوت"), ("Brennan", "برينان"),
    ("Chandler", "تشاندلر"), ("Delaney", "ديلاني"), ("Ellison", "إليسون"), ("Fletcher", "فليتشر"), ("Gilmour", "غيلمور"), ("Hadley", "هادلي"),
    ("Keller", "كيلر"), ("Lambert", "لامبرت"),
]

# name group -> (male, female, surname) slice sizes; see the module note before changing them.
GROUP_SIZES = {
    "arab": (14, 12, 13), "emirati": (8, 6, 8), "indian": (16, 10, 14), "pakistani": (10, 5, 9),
    "filipino": (8, 7, 9), "southasia": (8, 5, 9), "western": (5, 4, 6),
}

# Combinations that would read as a well-known person: the surname moves to the next one in its group's slice.
AVOID = {("Jack", "Harlow"), ("Helen", "Keller"), ("Kate", "Winslow"), ("Paul", "Weston"), ("Tom", "Fletcher")}


def _slices():
    out, at = {}, [0, 0, 0]
    for group, sizes in GROUP_SIZES.items():
        for k, (key, pool) in enumerate((("m", MALE), ("f", FEMALE), ("l", SURNAMES))):
            out[f"{group}_{key}"] = pool[at[k]:at[k] + sizes[k]]
            at[k] += sizes[k]
    assert at == [len(MALE), len(FEMALE), len(SURNAMES)], at
    return out


NAMES = _slices()
assert len({n for n, _ in MALE}) == len(MALE) and len({n for n, _ in FEMALE}) == len(FEMALE) and len({n for n, _ in SURNAMES}) == len(SURNAMES)


def pick(r, group, female):
    """Draws ((first, first Arabic), (last, last Arabic)) for a person of the nationality's name group: two draws, as before."""
    first = r.choice(NAMES[f"{group}_{'f' if female else 'm'}"])
    lasts = NAMES[f"{group}_l"]
    last = r.choice(lasts)
    while (first[0], last[0]) in AVOID:
        last = lasts[(lasts.index(last) + 1) % len(lasts)]
    return first, last
