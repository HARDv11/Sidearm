"""Gives the built exe a real build time and a correct PE checksum (Mono leaves both at zero)."""
import sys, time, pefile
path = sys.argv[1]
pe = pefile.PE(path)
pe.FILE_HEADER.TimeDateStamp = int(time.time())
pe.OPTIONAL_HEADER.CheckSum = 0
data = pe.write()
pe.close()
pe = pefile.PE(data=bytes(data))
pe.OPTIONAL_HEADER.CheckSum = pe.generate_checksum()
open(path, 'wb').write(pe.write())
print('stamped', path, time.strftime('%Y-%m-%d %H:%M', time.gmtime(pe.FILE_HEADER.TimeDateStamp)), 'checksum', hex(pe.OPTIONAL_HEADER.CheckSum))
