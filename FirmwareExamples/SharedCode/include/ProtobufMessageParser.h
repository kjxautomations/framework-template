#ifndef INC_LINEMESSAGEPARSER_H_
#define INC_LINEMESSAGEPARSER_H_
#include "firmware.pb.h"
#include <vector>

class ProtobufMessageParser 
{
	std::vector<uint8_t> _buffer;
public:
	std::vector<std::unique_ptr<Request>> ProcessBuffer(
			std::vector<uint8_t>::const_iterator start,
			std::vector<uint8_t>::const_iterator end);
	void ConnectionTerminated();
};




#endif /* INC_LINEMESSAGEPARSER_H_ */
