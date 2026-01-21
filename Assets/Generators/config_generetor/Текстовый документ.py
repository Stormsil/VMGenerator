import random
import uuid
import pyperclip
from datetime import datetime, timedelta

def generate_parameters():
    # 1. ГИПЕРВИЗОР (Безопасные)
    hv_vendor_ids = ['GenuineIntel', 'Microsoft Hv'] 
    
    # 2. ЖЕЛЕЗО (РАСШИРЕННАЯ БАЗА - Z590, B560, Z490, B460, H510, H410)
    # Все эти чипсеты используют Socket LGA1200, как и процессоры в списке ниже.
    hardware_db = {
        'ASUS': {
            'family': 'ASUS MB',
            'bios_vendor': 'ASUSTeK COMPUTER INC.',
            'products': [
                # Z590 / B560
                'ROG MAXIMUS XIII HERO', 'ROG STRIX Z590-E GAMING WIFI', 'TUF GAMING Z590-PLUS', 
                'PRIME Z590-A', 'ROG STRIX B560-F GAMING', 'ASUS Z590-I Gaming', 
                'ASUS TUF B560-PLUS WIFI', 'ASUS PRIME B560M-A',
                # Z490 / B460 (Тоже подходят идеально)
                'ROG MAXIMUS XII HERO', 'ROG STRIX Z490-E GAMING', 'TUF GAMING Z490-PLUS',
                'PRIME Z490-A', 'ROG STRIX B460-F GAMING', 'PRIME B460M-A',
                # H-series
                'PRIME H510M-E', 'PRIME H410M-K', 'TUF GAMING H570-PRO'
            ]
        },
        'MSI': {
            'family': 'MSI MB',
            'bios_vendor': 'American Megatrends Inc.',
            'products': [
                # Z590 / B560
                'MEG Z590 ACE', 'MPG Z590 GAMING CARBON WIFI', 'MAG Z590 TOMAHAWK WIFI',
                'Z590-A PRO', 'MAG B560 TOMAHAWK WIFI', 'MAG B560M MORTAR', 
                'B560M PRO-VDH',
                # Z490 / B460
                'MEG Z490 GODLIKE', 'MPG Z490 GAMING EDGE WIFI', 'MAG Z490 TOMAHAWK',
                'Z490-A PRO', 'MAG B460 TOMAHAWK', 'MAG B460M MORTAR WIFI',
                'H510M-A PRO', 'H410M PRO'
            ]
        },
        'Gigabyte': {
            'family': 'Gigabyte MB',
            'bios_vendor': 'Gigabyte Technology Co. Ltd.',
            'products': [
                # Z590 / B560
                'Z590 AORUS MASTER', 'Z590 AORUS ELITE AX', 'Z590 VISION G',
                'Z590 GAMING X', 'B560 AORUS PRO AX', 'B560M DS3H', 
                # Z490 / B460
                'Z490 AORUS XTREME', 'Z490 AORUS MASTER', 'Z490 VISION D',
                'Z490 UD', 'B460M AORUS PRO', 'B460M DS3H',
                'H510M S2H', 'H410M H'
            ]
        },
        'ASRock': {
            'family': 'ASRock MB',
            'bios_vendor': 'American Megatrends Inc.',
            'products': [
                'Z590 Taichi', 'Z590 Extreme', 'Z590 Steel Legend', 'Z590 Phantom Gaming 4',
                'B560 Steel Legend', 'B560M Pro4', 'B560M-HDV',
                'Z490 Taichi', 'Z490 Extreme4', 'Z490 Phantom Gaming 4',
                'B460 Steel Legend', 'B460M Pro4', 'H510M-HDV'
            ]
        },
        'HUANANZHI': { 
            'family': 'HUANANZHI MB',
            'bios_vendor': 'American Megatrends Inc.',
            'products': [
                'X99-TF', 'X99-F8', 'X99-T8', 'X99-AD4', 'X99-BD4', 'X99-8M',
                'X79-ZD3', 'X99-QD4' # Добавил еще парочку популярных
            ]
        },
        'EVGA': { # Добавил редкого вендора для разнообразия
             'family': 'EVGA MB',
             'bios_vendor': 'American Megatrends Inc.',
             'products': ['EVGA Z590 DARK', 'EVGA Z590 FTW WIFI', 'EVGA Z490 DARK', 'EVGA Z490 FTW']
        }
    }

    # Выбор железа
    selected_brand = random.choice(list(hardware_db.keys()))
    mb_data = hardware_db[selected_brand]
    
    vendor = mb_data['bios_vendor'] 
    manufacturer = selected_brand   
    product = random.choice(mb_data['products']) 
    family = mb_data['family']

    # BIOS
    if selected_brand == 'Gigabyte':
        # Gigabyte использует версии типа F2, F10, F20a
        bios_versions = [f'F{i}' for i in range(2, 15)] + ['F20', 'F21', 'F3a', 'F4c']
    elif selected_brand == 'ASRock':
        # ASRock часто использует P1.20, P2.10
        bios_versions = ['P1.10', 'P1.20', 'P1.40', 'P2.10', 'P2.50', 'L1.52', '1.80']
    elif selected_brand == 'MSI':
        # MSI использует 1.0, A.10 и т.д.
        bios_versions = ['1.0', '1.2', '1.4', '2.0', '2.3', 'A.10', 'A.20', '7.00']
    else: 
        # ASUS, HUANANZHI, EVGA (Обычно числовые 0403, 2001)
        bios_versions = ['1004', '1202', '1401', '1602', '2004', '2201', '2403', '0605', '0403']
    
    version = random.choice(bios_versions)
    bios_releases = ['5.17', '5.19', '5.22', '6.00', '6.41', '6.52']
    release = random.choice(bios_releases)
    
    # Дата BIOS
    current_date = datetime.now()
    days_ago = random.randint(200, 800) # Диапазон пошире
    bios_date = (current_date - timedelta(days=days_ago)).strftime('%m/%d/%Y')

    # 3. CPU (Идеальный микс под Xeon v3/v4 хост)
    cpu_list = [
        # HexWare Legacy (E3)
        'Intel(R) Xeon(R) CPU E3-1225 v3 @ 3.20GHz', 'Intel(R) Xeon(R) CPU E3-1230 v6 @ 3.50GHz',
        'Intel(R) Xeon(R) CPU E3-1240 v3 @ 3.40GHz', 'Intel(R) Xeon(R) CPU E3-1245 v5 @ 3.50GHz',
        'Intel(R) Xeon(R) CPU E3-1270 v5 @ 3.60GHz', 'Intel(R) Xeon(R) CPU E3-1280 v6 @ 3.90GHz',
        # Твои родные Haswell E5 v3/v4 (Socket 2011-3)
        'Intel(R) Xeon(R) CPU E5-2678 v3 @ 2.50GHz', 'Intel(R) Xeon(R) CPU E5-2666 v3 @ 2.90GHz',
        'Intel(R) Xeon(R) CPU E5-2640 v3 @ 2.60GHz', 'Intel(R) Xeon(R) CPU E5-2620 v3 @ 2.40GHz',
        'Intel(R) Xeon(R) CPU E5-2670 v3 @ 2.30GHz', 'Intel(R) Xeon(R) CPU E5-2680 v4 @ 2.40GHz',
        'Intel(R) Xeon(R) CPU E5-2650 v4 @ 2.20GHz', 'Intel(R) Xeon(R) CPU E5-2690 v3 @ 2.60GHz',
        # Добавим i7 Haswell/Broadwell (Тоже архитектурно подходят под хост)
        'Intel(R) Core(TM) i7-5960X CPU @ 3.00GHz', 'Intel(R) Core(TM) i7-6800K CPU @ 3.40GHz'
    ]
    version_xeon_choice = random.choice(cpu_list)

    # 4. RAM
    ram_db = [
        {'mfg': 'Samsung', 'part': 'M393A1G40DDA-CPB', 'serial_suffix': 'T6'},
        {'mfg': 'G.Skill', 'part': 'F4-3200C16D-32GTZR', 'serial_suffix': 'GS'},
        {'mfg': 'SK Hynix', 'part': 'HMA81GS6CJR8N-VK', 'serial_suffix': 'HY'},
        {'mfg': 'Corsair', 'part': 'CMK32GX4M2B3200C16', 'serial_suffix': 'CS'},
        {'mfg': 'Crucial', 'part': 'BL2K16G32C16U4B', 'serial_suffix': 'CR'},
        {'mfg': 'Kingston', 'part': 'HX432C16FB3K2/32', 'serial_suffix': 'KG'},
        {'mfg': 'Patriot', 'part': 'PVS416G320C6K', 'serial_suffix': 'PA'}, # Добавил Patriot
        {'mfg': 'TeamGroup', 'part': 'TF3D416G3200HC16CDC01', 'serial_suffix': 'TG'} # Добавил TeamGroup
    ]
    ram_choice = random.choice(ram_db)
    
    # Генерация ID
    hv_vendor_id = random.choice(hv_vendor_ids)
    generated_uuid = str(uuid.uuid4())
    serial_sys = f'SN-{random.randint(100000000000, 999999999999)}'
    serial_mb = f'MB-{random.randint(100000000000, 999999999999)}'
    serial_chassis = f'SN-{random.randint(100000000000, 999999999999)}'
    serial_ram = f'{random.randint(100000, 999999)}{ram_choice["serial_suffix"]}'
    
    # ПОРТ VNC
    vnc_string = '0.0.0.0:00' 

    # Сборка строки
    params = (
        f"args: -cpu 'host,hypervisor=off,kvm=off,rdtscp=off,migratable=off,hv-vendor-id={hv_vendor_id}' "
        f"-smbios 'type=0,version=BIOS Date: {bios_date} Ver: {version},vendor={vendor},uefi=on,release={release},date=AMI {bios_date}' "
        f"-smbios 'type=1,version=1.0,product={product},manufacturer={manufacturer},uuid={generated_uuid},serial={serial_sys},family={family}' "
        f"-smbios 'type=2,asset=Not Specified,version=1.0,product={product},location=Motherboard,manufacturer={manufacturer},serial={serial_mb}' "
        f"-smbios 'type=3,asset=Not Specified,version=2021,sku=Default string,manufacturer={manufacturer},serial={serial_chassis}' "
        f"-smbios 'type=4,asset=Not Specified,version={version_xeon_choice},part=Xeon,manufacturer=Intel,serial=Not Specified,sock_pfx=SOCKET 0' "
        f"-smbios 'type=11,value=To be filled by O.E.M.' "
        f"-smbios 'type=17,bank=Bank 0,asset=DIMM_A1_AssetTag,part={ram_choice['part']},manufacturer={ram_choice['mfg']},speed=2666,serial={serial_ram},loc_pfx=DIMM 0' "
        f"-vnc '{vnc_string}'"
    )
    
    # Сохранение и вывод
    with open('generated_parameters.txt', 'w') as f:
        f.write(params)
    pyperclip.copy(params)
    print(params)

# Запуск
generate_parameters()