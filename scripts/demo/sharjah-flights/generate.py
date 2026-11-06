"""Generates a realistic, synthetic 2025 flight-movement dataset for Sharjah International Airport (SHJ).
Route network, carriers and fleet mirror the real airport; the individual movements are simulated."""
import csv, random, datetime as dt, math, os
random.seed(2025)
OUT = os.path.dirname(os.path.abspath(__file__))

airlines = [
 # code, name, country, share weight, fleet (type, seats)
 ("G9","Air Arabia","United Arab Emirates",62,[("A320-214",168),("A320-214",168),("A321neo",232),("A321-200",215)]),
 ("6E","IndiGo","India",7,[("A320neo",186),("A321neo",232)]),
 ("IX","Air India Express","India",5,[("B737-800",189),("B737 MAX 8",189)]),
 ("PK","Pakistan International Airlines","Pakistan",3,[("A320-200",180),("B777-200ER",329)]),
 ("PA","airblue","Pakistan",2,[("A320-200",180),("A321-200",220)]),
 ("BG","Biman Bangladesh Airlines","Bangladesh",2,[("B737-800",162),("B787-8",271)]),
 ("BS","US-Bangla Airlines","Bangladesh",1.5,[("B737-800",189)]),
 ("J9","Jazeera Airways","Kuwait",2,[("A320neo",165)]),
 ("OV","SalamAir","Oman",1.5,[("A320neo",180),("A321neo",212)]),
 ("PC","Pegasus Airlines","Türkiye",1.5,[("A321neo",239)]),
 ("MS","EgyptAir","Egypt",1.5,[("A320neo",142),("B737-800",154)]),
 ("TK","Turkish Airlines","Türkiye",1,[("A321-200",180)]),
 ("UL","SriLankan Airlines","Sri Lanka",1,[("A320neo",150)]),
 ("IR","Iran Air","Iran",1,[("A320-200",150)]),
 ("IF","Fly Baghdad","Iraq",1,[("B737-800",189)]),
 ("RJ","Royal Jordanian","Jordan",1,[("A320-200",150),("E195",120)]),
 ("ME","Middle East Airlines","Lebanon",0.8,[("A320-200",150)]),
 ("WY","Oman Air","Oman",0.8,[("B737 MAX 8",174)]),
 ("H9","Himalaya Airlines","Nepal",0.6,[("A320-200",158)]),
 ("ET","Ethiopian Airlines","Ethiopia",0.6,[("B737-800",154)]),
 ("UT","UTair","Russia",0.6,[("B737-800",189)]),
 ("HY","Uzbekistan Airways","Uzbekistan",0.6,[("A320neo",150)]),
 ("Y7","NordStar","Russia",0.4,[("B737-800",189)]),
]
# IATA, name, city, country, region, relative demand weight (per direction), block minutes
airports = [
 ("CAI","Cairo International","Cairo","Egypt","Middle East & North Africa",6,225),
 ("HBE","Borg El Arab","Alexandria","Egypt","Middle East & North Africa",2.5,240),
 ("BOM","Chhatrapati Shivaji Maharaj International","Mumbai","India","South Asia",5,195),
 ("DEL","Indira Gandhi International","Delhi","India","South Asia",4,215),
 ("COK","Cochin International","Kochi","India","South Asia",4.5,255),
 ("CCJ","Calicut International","Kozhikode","India","South Asia",3.5,250),
 ("TRV","Trivandrum International","Thiruvananthapuram","India","South Asia",3,260),
 ("MAA","Chennai International","Chennai","India","South Asia",2.5,250),
 ("HYD","Rajiv Gandhi International","Hyderabad","India","South Asia",3,220),
 ("BLR","Kempegowda International","Bengaluru","India","South Asia",2.5,240),
 ("AMD","Sardar Vallabhbhai Patel International","Ahmedabad","India","South Asia",2,180),
 ("LKO","Chaudhary Charan Singh International","Lucknow","India","South Asia",2,225),
 ("JAI","Jaipur International","Jaipur","India","South Asia",1.5,200),
 ("CNN","Kannur International","Kannur","India","South Asia",1.2,245),
 ("KHI","Jinnah International","Karachi","Pakistan","South Asia",4,125),
 ("LHE","Allama Iqbal International","Lahore","Pakistan","South Asia",3.5,195),
 ("ISB","Islamabad International","Islamabad","Pakistan","South Asia",3,205),
 ("PEW","Bacha Khan International","Peshawar","Pakistan","South Asia",2.5,200),
 ("SKT","Sialkot International","Sialkot","Pakistan","South Asia",2,195),
 ("MUX","Multan International","Multan","Pakistan","South Asia",1.5,170),
 ("DAC","Hazrat Shahjalal International","Dhaka","Bangladesh","South Asia",3.5,300),
 ("CGP","Shah Amanat International","Chittagong","Bangladesh","South Asia",2,300),
 ("KTM","Tribhuvan International","Kathmandu","Nepal","South Asia",3,255),
 ("CMB","Bandaranaike International","Colombo","Sri Lanka","South Asia",2,255),
 ("JED","King Abdulaziz International","Jeddah","Saudi Arabia","GCC",3.5,185),
 ("RUH","King Khalid International","Riyadh","Saudi Arabia","GCC",3,120),
 ("DMM","King Fahd International","Dammam","Saudi Arabia","GCC",2,75),
 ("MED","Prince Mohammad bin Abdulaziz International","Medina","Saudi Arabia","GCC",1.5,180),
 ("DOH","Hamad International","Doha","Qatar","GCC",2.5,70),
 ("KWI","Kuwait International","Kuwait City","Kuwait","GCC",3,105),
 ("BAH","Bahrain International","Manama","Bahrain","GCC",2,70),
 ("MCT","Muscat International","Muscat","Oman","GCC",2.5,65),
 ("SLL","Salalah","Salalah","Oman","GCC",1,110),
 ("AMM","Queen Alia International","Amman","Jordan","Middle East & North Africa",2.5,190),
 ("BEY","Beirut–Rafic Hariri International","Beirut","Lebanon","Middle East & North Africa",2,230),
 ("BGW","Baghdad International","Baghdad","Iraq","Middle East & North Africa",2,150),
 ("BSR","Basra International","Basra","Iraq","Middle East & North Africa",1.2,120),
 ("NJF","Al Najaf International","Najaf","Iraq","Middle East & North Africa",1.5,150),
 ("EBL","Erbil International","Erbil","Iraq","Middle East & North Africa",1,165),
 ("IKA","Tehran Imam Khomeini International","Tehran","Iran","Middle East & North Africa",1.5,120),
 ("SYZ","Shiraz International","Shiraz","Iran","Middle East & North Africa",1,70),
 ("MHD","Mashhad International","Mashhad","Iran","Middle East & North Africa",1,120),
 ("KBL","Kabul International","Kabul","Afghanistan","South Asia",1,180),
 ("IST","Istanbul Airport","Istanbul","Türkiye","Europe",2,280),
 ("SAW","Istanbul Sabiha Gökçen","Istanbul","Türkiye","Europe",2,275),
 ("AYT","Antalya","Antalya","Türkiye","Europe",1,260),
 ("TZX","Trabzon","Trabzon","Türkiye","Europe",0.6,210),
 ("GYD","Heydar Aliyev International","Baku","Azerbaijan","Caucasus & Central Asia",1.5,175),
 ("TBS","Tbilisi International","Tbilisi","Georgia","Caucasus & Central Asia",1.2,200),
 ("EVN","Zvartnots International","Yerevan","Armenia","Caucasus & Central Asia",1,195),
 ("ALA","Almaty International","Almaty","Kazakhstan","Caucasus & Central Asia",1,270),
 ("TAS","Tashkent International","Tashkent","Uzbekistan","Caucasus & Central Asia",1,215),
 ("FRU","Manas International","Bishkek","Kyrgyzstan","Caucasus & Central Asia",0.6,240),
 ("SVO","Sheremetyevo","Moscow","Russia","Europe",1.5,330),
 ("DME","Domodedovo","Moscow","Russia","Europe",1,330),
 ("KZN","Kazan International","Kazan","Russia","Europe",0.6,290),
 ("UFA","Ufa International","Ufa","Russia","Europe",0.5,270),
 ("NBO","Jomo Kenyatta International","Nairobi","Kenya","Africa",1,300),
 ("ADD","Addis Ababa Bole International","Addis Ababa","Ethiopia","Africa",0.8,235),
 ("CMN","Mohammed V International","Casablanca","Morocco","Middle East & North Africa",0.8,450),
 ("MLE","Velana International","Malé","Maldives","South Asia",1,255),
 ("PRG","Václav Havel Airport Prague","Prague","Czechia","Europe",0.6,390),
 ("VIE","Vienna International","Vienna","Austria","Europe",0.6,365),
 ("ATH","Athens International","Athens","Greece","Europe",0.8,300),
 ("BGY","Milan Bergamo","Milan","Italy","Europe",0.7,395),
 ("SJJ","Sarajevo International","Sarajevo","Bosnia and Herzegovina","Europe",0.5,330),
 ("HKT","Phuket International","Phuket","Thailand","Asia Pacific",0.8,390),
 ("KUL","Kuala Lumpur International","Kuala Lumpur","Malaysia","Asia Pacific",0.6,420),
]
# which airlines serve which regions (Air Arabia serves everything)
serve = {
 "6E":{"BOM","DEL","COK","CCJ","TRV","MAA","HYD","BLR","AMD","LKO","JAI","CNN"},
 "IX":{"COK","CCJ","TRV","MAA","BLR","CNN","HYD"},
 "PK":{"KHI","LHE","ISB","PEW","SKT","MUX"}, "PA":{"KHI","LHE","ISB","PEW","SKT","MUX"},
 "BG":{"DAC","CGP"}, "BS":{"DAC","CGP"}, "J9":{"KWI"}, "OV":{"MCT","SLL"}, "PC":{"SAW","AYT"},
 "MS":{"CAI","HBE"}, "TK":{"IST"}, "UL":{"CMB"}, "IR":{"IKA","SYZ","MHD"}, "IF":{"BGW","BSR","NJF"},
 "RJ":{"AMM"}, "ME":{"BEY"}, "WY":{"MCT"}, "H9":{"KTM"}, "ET":{"ADD"}, "UT":{"SVO","UFA"}, "HY":{"TAS"}, "Y7":{"KZN"},
}
month_factor = {1:1.02,2:0.93,3:1.0,4:1.0,5:0.95,6:1.03,7:1.12,8:1.14,9:0.96,10:0.98,11:0.97,12:1.10}
dow_factor = {0:0.96,1:0.93,2:0.94,3:1.0,4:1.08,5:1.05,6:1.04}  # Mon..Sun
eid = [dt.date(2025,3,28),dt.date(2025,3,29),dt.date(2025,3,30),dt.date(2025,3,31),dt.date(2025,4,1),dt.date(2025,6,5),dt.date(2025,6,6),dt.date(2025,6,7),dt.date(2025,6,8)]
hours_dep = [0.6,0.9,1.3,1.4,1.0,0.6,0.9,1.3,1.4,1.2,1.0,0.8,0.7,0.9,1.1,1.2,1.0,0.9,1.0,1.1,1.2,1.3,1.1,0.8]  # departure wave
hours_arr = [1.0,0.7,0.4,0.5,0.7,1.0,1.1,1.0,0.8,0.7,0.8,1.0,1.2,1.3,1.2,1.0,0.9,1.0,1.1,1.0,1.2,1.3,1.4,1.2]

def pick(weights):
    r=random.random()*sum(w for _,w in weights)
    for item,w in weights:
        r-=w
        if r<=0: return item
    return weights[-1][0]

air_w=[(a,a[3]) for a in airlines]
apt_by=[(ap,ap[5]) for ap in airports]
code_of={ap[0]:ap for ap in airports}

flights=[]
fid=1
day=dt.date(2025,1,1)
while day.year==2025:
    base=176  # per direction per day
    f=month_factor[day.month]*dow_factor[day.weekday()]*(1.18 if day in eid else 1.0)*random.uniform(0.95,1.05)
    n_dir=int(round(base*f))
    for direction in ("Departure","Arrival"):
        hours=hours_dep if direction=="Departure" else hours_arr
        for _ in range(n_dir):
            ap=pick(apt_by)
            # airline eligible for this airport
            cand=[(a,w) for a,w in air_w if a[0]=="G9" or ap[0] in serve.get(a[0],set())]
            al=pick(cand)
            ac,seats=random.choice(al[4])
            h=pick([(i,w) for i,w in enumerate(hours)])
            m=random.choice([0,5,10,15,20,25,30,35,40,45,50,55])
            sched=dt.datetime(day.year,day.month,day.day,h,m)
            # delay model: season, fog mornings Jan/Feb, evening congestion
            mu=9+ (6 if day.month in (7,8) else 0) + (10 if day.month in (1,2,12) and 4<=h<=9 else 0) + (4 if 18<=h<=23 else 0)
            delay=max(0,int(random.gauss(mu,18)))
            if random.random()<0.6: delay=max(0,delay-10)
            status="Cancelled" if random.random()<0.011 else ("Diverted" if random.random()<0.0015 else ("Delayed" if delay>15 else "On Time"))
            actual=None if status=="Cancelled" else sched+dt.timedelta(minutes=delay if status!="Diverted" else delay+95)
            lf=random.gauss(0.83 if day.month in (7,8,12) or day in eid else 0.78,0.07)
            pax=0 if status=="Cancelled" else int(seats*min(max(lf,0.35),1.0))
            flightno=f"{al[0]}{random.randint(101,999) if al[0]!='G9' else random.randint(101,699)}"
            terminal="T1" if al[0]=="G9" else "T2"
            flights.append([fid,flightno,al[0],direction,ap[0],sched.strftime("%Y-%m-%d %H:%M:00"),
                            actual.strftime("%Y-%m-%d %H:%M:00") if actual else "",delay if status!="Cancelled" else "",
                            status,ac,seats,pax,terminal,f"{random.choice('ABC')}{random.randint(1,24)}",
                            day.isoformat(),day.strftime("%A"),day.weekday()+1,day.month,day.strftime("%B"),(day.month-1)//3+1,h,ap[6]])
            fid+=1
    day+=dt.timedelta(days=1)

with open(os.path.join(OUT,"flights.csv"),"w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh,lineterminator="\n")
    for r in flights: w.writerow(r)
with open(os.path.join(OUT,"airlines.csv"),"w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh,lineterminator="\n")
    for a in airlines: w.writerow([a[0],a[1],a[2]])
with open(os.path.join(OUT,"airports.csv"),"w",newline="",encoding="utf-8") as fh:
    w=csv.writer(fh,lineterminator="\n")
    for ap in airports: w.writerow([ap[0] if ap[0]!="COK2" else "CNN",ap[1],ap[2],ap[3],ap[4]])
# fix CNN code in flights
print(len(flights),"flights;", sum(1 for f in flights if f[8]=="Cancelled"),"cancelled")
