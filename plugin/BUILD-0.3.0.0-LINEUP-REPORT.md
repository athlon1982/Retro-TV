# Plugin 0.3.0.0 — lineup + seed generation report

- Source pools: 155 channels, 3032 items
- Lineup defs written: 155 (numbers 1–216, gaps where the sidecar has none)
- Seed pool channels: 153 / 2982 items (weather + audiobooks excluded)
- Catalog refresh: 72 channels have a catalog folder (refresh merges catalog items in); 81 have Folder = own name (refresh leaves their seed pools untouched)
- Group sources: {'group:build-pools.mjs': 44, 'seed-channels': 153, 'seed-items': 2982, 'refresh-enabled': 72, 'refresh-frozen': 81, 'group:current-lineup': 24, 'group:fallback-rule': 85, 'group:DEFAULT': 1, 'audiobooks-excluded': 1, 'group:add-backwhen-channels.py': 1, 'weather-synthetic': 1}

## Per-channel provenance

| # | Name | Group (source) | Folder | Keywords |
|---:|---|---|---|---|
| 1 | Saturday Morning Cartoons | Kids & cartoons | Saturday Morning Cartoons (25/25 in catalog) | — |
| 2 | Looney Tunes | Kids & cartoons | Looney Tunes (0/25 in catalog) | title=looney tunes |
| 3 | Toonami | Kids & cartoons | Toonami (12/25 in catalog) | title=toonami |
| 4 | Nickelodeon | Kids & cartoons | Nickelodeon (0/25 in catalog) | title=nickelodeon |
| 5 | Popeye | Kids & cartoons | Saturday Morning Cartoons (6/25 in catalog) | title=popeye |
| 6 | Betty Boop | Kids & cartoons | Saturday Morning Cartoons (19/25 in catalog) | title=betty boop |
| 7 | Felix the Cat | Kids & cartoons | Saturday Morning Cartoons (7/25 in catalog) | title=felix |
| 8 | Superman 1940s | Kids & cartoons | Superman 1940s (0/25 in catalog) | title=superman |
| 9 | Classic TV | Sitcoms & classic TV | Classic TV (25/25 in catalog) | — |
| 10 | Hit Sitcoms | Sitcoms & classic TV | Classic TV (3/25 in catalog) | genre=sitcom,situation comedy |
| 11 | Buzzer Row | Sitcoms & classic TV | Classic TV (1/25 in catalog) | genre=game show |
| 12 | Variety Show | Sitcoms & classic TV | Classic TV (1/25 in catalog) | genre=variety |
| 13 | Action Night | Action & adventure TV | Classic TV (2/25 in catalog) | genre=action |
| 14 | Knight Rider | Action & adventure TV | Knight Rider (0/25 in catalog) | title=knight rider |
| 15 | Detective TV | Drama, mystery & sci-fi | Classic TV (2/25 in catalog) | genre=detective |
| 16 | Sci-Fi TV | Drama, mystery & sci-fi | Classic TV (1/25 in catalog) | genre=science fiction |
| 17 | Twilight Zone | Drama, mystery & sci-fi | Twilight Zone (0/25 in catalog) | title=twilight zone |
| 18 | Hitchcock | Drama, mystery & sci-fi | Hitchcock (0/25 in catalog) | title=hitchcock |
| 19 | Dynasty | Drama, mystery & sci-fi | Dynasty (0/25 in catalog) | title=dynasty |
| 20 | Bonanza | Drama, mystery & sci-fi | Bonanza (0/25 in catalog) | title=bonanza |
| 21 | Music Videos | Music | Music Videos (0/25 in catalog) | genre=music video |
| 22 | Rock Videos | Music | Rock Videos (0/25 in catalog) | genre=music video,rock |
| 23 | Old School Hip-Hop | Music | Old School Hip-Hop (0/25 in catalog) | genre=hip-hop |
| 24 | Jazz Club | Music | Jazz Club (0/25 in catalog) | genre=jazz |
| 25 | Country Music | Music | Country Music (0/25 in catalog) | genre=country music |
| 26 | Music Videos 80s | Music | Music Videos 80s (0/24 in catalog) | genre=music video |
| 27 | Music Videos 90s | Music | Music Videos 90s (0/25 in catalog) | genre=music video |
| 28 | Drive-In Movies | Movies | Drive-In Movies (25/25 in catalog) | — |
| 29 | Film Noir | Movies | Drive-In Movies (3/25 in catalog) | genre=noir |
| 30 | Silent Films | Movies | Drive-In Movies (6/25 in catalog) | genre=silent |
| 31 | Westerns | Movies | Drive-In Movies (5/25 in catalog) | genre=western |
| 32 | Creature Features | Movies | Creature Features (25/25 in catalog) | genre=horror,science fiction |
| 33 | Three Stooges | Movies | Three Stooges (0/25 in catalog) | title=stooges |
| 34 | Chaplin | Movies | Chaplin (0/25 in catalog) | title=chaplin |
| 35 | Movie Trailers | Movies | Movie Trailers (0/25 in catalog) | — |
| 36 | General Hospital | Soap operas & novelas | Soaps (20/22 in catalog) | title=general hospital |
| 37 | Dark Shadows | Soap operas & novelas | Soaps (1/22 in catalog) | title=dark shadows |
| 38 | Guiding Light | Soap operas & novelas | Soaps (14/25 in catalog) | title=guiding light |
| 39 | All My Children | Soap operas & novelas | All My Children (0/25 in catalog) | title=all my children |
| 40 | Soaps | Soap operas & novelas | Soaps (4/24 in catalog) | title=general hospital,guiding light,dark shadows,days of our lives,young and the restless |
| 41 | Julia Child | Home, food & fitness | Julia Child (0/25 in catalog) | title=julia child |
| 42 | This Old House | Home, food & fitness | This Old House (0/25 in catalog) | title=this old house |
| 43 | Boxing | Sports | Boxing (0/25 in catalog) | genre=boxing |
| 44 | Olympics | Sports | Olympics (0/25 in catalog) | genre=olympics |
| 45 | Newsreels | News, nature & science | Newsreels (0/25 in catalog) | — |
| 46 | World War II | News, nature & science | Prelinger Theater (11/25 in catalog) | genre=world war II |
| 47 | NASA TV | News, nature & science | NASA TV (0/25 in catalog) | title=nasa |
| 48 | Computer Chronicles | News, nature & science | Computer Chronicles (0/25 in catalog) | title=computer chronicles |
| 49 | Prelinger Theater | Commercials, vaults & home video | Prelinger Theater (25/25 in catalog) | — |
| 50 | Ad Break | Commercials, vaults & home video | Ad Break (25/25 in catalog) | — |
| 51 | Ads: 1950s | Commercials, vaults & home video | Ad Break (4/22 in catalog) | — |
| 52 | Ads: 1980s | Commercials, vaults & home video | Ad Break (2/18 in catalog) | — |
| 53 | Ads: 1990s | Commercials, vaults & home video | Ad Break (2/16 in catalog) | — |
| 54 | Infomercials | Commercials, vaults & home video | Infomercials (0/25 in catalog) | genre=infomercial |
| 55 | School Days | Commercials, vaults & home video | Prelinger Theater (4/25 in catalog) | genre=education |
| 56 | VHS Vault 1990 | Commercials, vaults & home video | VHS Vault (1/3 in catalog) | year=1990 |
| 57 | VHS Vault 1991 | Commercials, vaults & home video | VHS Vault 1991 (0/25 in catalog) | year=1991 |
| 58 | VHS Vault 1992 | Commercials, vaults & home video | VHS Vault (1/25 in catalog) | year=1992 |
| 59 | VHS Vault 1993 | Commercials, vaults & home video | VHS Vault (1/25 in catalog) | year=1993 |
| 60 | VHS Vault 1994 | Commercials, vaults & home video | VHS Vault (1/7 in catalog) | year=1994 |
| 61 | VHS Vault 1995 | Commercials, vaults & home video | VHS Vault (1/25 in catalog) | year=1995 |
| 62 | VHS Vault 1996 | Commercials, vaults & home video | VHS Vault (2/25 in catalog) | year=1996 |
| 63 | VHS Vault 1997 | Commercials, vaults & home video | VHS Vault (1/25 in catalog) | year=1997 |
| 64 | VHS Vault 1998 | Commercials, vaults & home video | VHS Vault (1/25 in catalog) | year=1998 |
| 65 | VHS Vault 1999 | Commercials, vaults & home video | VHS Vault (1/4 in catalog) | year=1999 |
| 66 | VHS Vault | Commercials, vaults & home video | VHS Vault (24/25 in catalog) | — |
| 67 | Audiobooks | Audiobooks | Audiobooks (0/25 in catalog) | — |
| 68 | NASA | News, nature & science | NASA (0/25 in catalog) | — |
| 83 | Haunted Drive-In | Movies | Creature Features (25/25 in catalog) | genre=horror,zombie,monster,ghost,vampire,frankenstein,haunted,terror,devil,demon,curse,mummy,werewolf,undead |
| 85 | Sci-Fi Theater | Movies | Creature Features (7/25 in catalog) | genre=science fiction,sci-fi,space,alien,robot,monster,mutant,invasion,planet,rocket |
| 89 | Laurel & Hardy | Movies | Laurel & Hardy (0/5 in catalog) | — |
| 125 | Weather | News, nature & science | — (0/25 in catalog) | — |
| 132 | Ads: 1970s | Commercials, vaults & home video | Ad Break (1/5 in catalog) | — |
| 133 | Ads: 1960s | Commercials, vaults & home video | Ad Break (1/9 in catalog) | — |
| 134 | MTV | Music | MTV (0/12 in catalog) | — |
| 135 | VEVO 90s | Music | VEVO 90s (0/10 in catalog) | — |
| 136 | Yo! MTV | Music | Yo! MTV (0/1 in catalog) | — |
| 137 | Heavy Rotation | Music | Heavy Rotation (0/23 in catalog) | — |
| 138 | VEVO 80s | Music | VEVO 80s (0/4 in catalog) | — |
| 139 | VEVO 2000s | Music | VEVO 2000s (0/21 in catalog) | — |
| 140 | VEVO 70s | Music | VEVO 70s (0/7 in catalog) | — |
| 141 | VEVO Pop | Music | VEVO Pop (0/13 in catalog) | — |
| 143 | VEVO Rock | Music | VEVO Rock (0/15 in catalog) | — |
| 144 | Rock & Metal | Music | Rock & Metal (0/16 in catalog) | — |
| 145 | VEVO Country | Music | VEVO Country (0/25 in catalog) | — |
| 147 | CMT | Music | CMT (0/17 in catalog) | — |
| 148 | Front Row Live | Music | Front Row Live (0/1 in catalog) | — |
| 149 | Live Concerts | Music | Live Concerts (0/4 in catalog) | — |
| 150 | Rock & Roll | Music | Rock & Roll (0/25 in catalog) | — |
| 151 | Jazz Videos | Music | Jazz Videos (0/20 in catalog) | — |
| 152 | 90s Kids | Kids & cartoons | 90s Kids (0/25 in catalog) | — |
| 153 | 90s Kids 2 | Kids & cartoons | 90s Kids 2 (0/19 in catalog) | — |
| 154 | Mecha Afternoon | Kids & cartoons | Mecha Afternoon (0/25 in catalog) | — |
| 155 | Toy Box Heroes | Kids & cartoons | Ad Break (6/6 in catalog) | — |
| 156 | Cape & Cowl | Kids & cartoons | Saturday Morning Cartoons (3/24 in catalog) | — |
| 157 | Mister Rogers | Kids & cartoons | Mister Rogers (0/15 in catalog) | — |
| 158 | Reading Rainbow | Kids & cartoons | Reading Rainbow (0/25 in catalog) | — |
| 159 | Saturday Morning Live | Kids & cartoons | Saturday Morning Live (0/25 in catalog) | — |
| 160 | Anime Zone | Kids & cartoons | Anime Zone (0/16 in catalog) | — |
| 161 | Storytime Station | Kids & cartoons | Prelinger Theater (8/8 in catalog) | — |
| 162 | Little Learners | Kids & cartoons | Prelinger Theater (7/17 in catalog) | — |
| 163 | Family Room Afternoons | Kids & cartoons | Saturday Morning Cartoons (16/16 in catalog) | — |
| 164 | Toon Lab | Kids & cartoons | Saturday Morning Cartoons (15/15 in catalog) | — |
| 165 | Pajama Saturday | Kids & cartoons | Saturday Morning Cartoons (22/22 in catalog) | — |
| 166 | Orange Couch | Kids & cartoons | Orange Couch (0/25 in catalog) | — |
| 167 | Kids TV | Kids & cartoons | Kids TV (0/25 in catalog) | — |
| 168 | Kids Classics | Kids & cartoons | Kids Classics (0/25 in catalog) | — |
| 169 | Cartoon Club | Kids & cartoons | Prelinger Theater (1/25 in catalog) | — |
| 170 | 90s Throwback | Kids & cartoons | 90s Throwback (0/7 in catalog) | — |
| 171 | TGIF Sitcoms | Sitcoms & classic TV | TGIF Sitcoms (0/7 in catalog) | — |
| 172 | Sitcom TV | Sitcoms & classic TV | Classic TV (25/25 in catalog) | — |
| 173 | Rabbit Ears | Sitcoms & classic TV | Classic TV (25/25 in catalog) | — |
| 174 | Primetime Tapes | Sitcoms & classic TV | Classic TV (25/25 in catalog) | — |
| 175 | Cable Classics After Dark | Sitcoms & classic TV | Classic TV (25/25 in catalog) | — |
| 176 | Talk Show | Sitcoms & classic TV | Talk Show (0/22 in catalog) | — |
| 177 | Back Roads & Backfires | Action & adventure TV | Prelinger Theater (23/23 in catalog) | — |
| 178 | Cops & Cruises | Action & adventure TV | Classic TV (1/25 in catalog) | — |
| 179 | Classic TV Drama | Drama, mystery & sci-fi | Classic TV Drama (0/25 in catalog) | — |
| 180 | TV Land Drama | Drama, mystery & sci-fi | Classic TV (22/22 in catalog) | — |
| 181 | Strange Tales Theater | Drama, mystery & sci-fi | Strange Tales Theater (0/24 in catalog) | — |
| 182 | Badge & Trench Coat | Action & adventure TV | Badge & Trench Coat (0/25 in catalog) | — |
| 183 | Rocketship Theater | Drama, mystery & sci-fi | Creature Features (1/24 in catalog) | — |
| 184 | Tumbleweed Theater | Movies | Tumbleweed Theater (0/25 in catalog) | — |
| 185 | Western Heroes | Movies | Western Heroes (0/23 in catalog) | — |
| 186 | VEVO R&B | Music | VEVO R&B (0/25 in catalog) | — |
| 187 | Classic Country | Music | Classic Country (0/22 in catalog) | — |
| 188 | Dragon Dojo | Movies | Dragon Dojo (0/10 in catalog) | — |
| 189 | Funny Pages Cinema | Movies | Drive-In Movies (16/16 in catalog) | — |
| 190 | Flicker Hall | Movies | Drive-In Movies (2/25 in catalog) | — |
| 191 | B-Movie | Movies | Drive-In Movies (19/19 in catalog) | — |
| 192 | As the World Turns | Soap operas & novelas | As the World Turns (0/25 in catalog) | — |
| 193 | Another World | Soap operas & novelas | Another World (0/25 in catalog) | — |
| 194 | Edge of Night | Soap operas & novelas | Edge of Night (0/23 in catalog) | — |
| 195 | Classic Soaps | Soap operas & novelas | Soaps (25/25 in catalog) | — |
| 196 | Soap Opera Mix | Soap operas & novelas | Soaps (25/25 in catalog) | — |
| 197 | Soap Opera Vault | Soap operas & novelas | Soaps (2/2 in catalog) | — |
| 198 | Telenovelas | Soap operas & novelas | Telenovelas (0/25 in catalog) | — |
| 199 | Novelas | Soap operas & novelas | Novelas (0/11 in catalog) | — |
| 200 | Telenovelas Clasicas | Soap operas & novelas | Telenovelas Clasicas (0/3 in catalog) | — |
| 201 | Home Improvement | Home, food & fitness | Prelinger Theater (17/17 in catalog) | — |
| 202 | Garden Channel | Home, food & fitness | Prelinger Theater (1/3 in catalog) | — |
| 203 | Do It Yourself | Home, food & fitness | Prelinger Theater (1/12 in catalog) | — |
| 204 | Interior Design | Home, food & fitness | Interior Design (0/1 in catalog) | — |
| 205 | Quilt & Sew | Home, food & fitness | Prelinger Theater (1/1 in catalog) | — |
| 206 | Garage TV | Home, food & fitness | Prelinger Theater (2/3 in catalog) | — |
| 207 | Workout TV | Home, food & fitness | Workout TV (0/1 in catalog) | — |
| 208 | Travelogue | Home, food & fitness | Prelinger Theater (3/7 in catalog) | — |
| 209 | Seventh Inning Stretch | Sports | Seventh Inning Stretch (0/3 in catalog) | — |
| 210 | Turnbuckle TV | Sports | Turnbuckle TV (0/20 in catalog) | — |
| 211 | 90s News | News, nature & science | 90s News (0/13 in catalog) | — |
| 212 | C-SPAN | News, nature & science | C-SPAN (0/11 in catalog) | — |
| 213 | Election Night | News, nature & science | Prelinger Theater (1/2 in catalog) | — |
| 214 | Public Affairs | News, nature & science | Public Affairs (0/14 in catalog) | — |
| 215 | Trail & Treetops | News, nature & science | Prelinger Theater (3/12 in catalog) | — |
| 216 | Station Break | Commercials, vaults & home video | Station Break (0/1 in catalog) | — |

## Notes / warnings

  GROUP KEPT prelinger-theater: current 'Commercials, vaults & home video' (script said 'News, nature & science')
