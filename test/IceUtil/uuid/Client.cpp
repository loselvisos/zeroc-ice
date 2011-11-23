// **********************************************************************
//
// Copyright (c) 2003-2004 ZeroC, Inc. All rights reserved.
//
// This copy of Ice is licensed to you under the terms described in the
// ICE_LICENSE file included in this distribution.
//
// **********************************************************************

#include <IceUtil/UUID.h>
#include <IceUtil/Time.h>
#include <TestCommon.h>
#include <set>

using namespace IceUtil;
using namespace std;


inline void usage(const char* myName)
{
    cerr << "Usage: " << myName << " [number of UUIDs to generate]" << endl;
}

int main(int argc, char* argv[])
{

    long howMany = 10000;
    bool verbose = false;

    if(argc > 2)
    {
	usage(argv[0]);
	return EXIT_FAILURE;
    }
    else if(argc == 2)
    {
	howMany = atol(argv[1]);
	if (howMany == 0)
	{
	    usage(argv[0]);
	    return EXIT_FAILURE;
	}
	verbose = true;
    }
    
    cout << "Generating " << howMany << " UUIds ... ";

    set<string> uuidSet;
    
    Time start = Time::now();
    for(long i = 0; i < howMany; i++)
    {
	pair<set<string>::iterator, bool> ok
	    = uuidSet.insert(generateUUID());
	test(ok.second);
    }
    Time finish = Time::now();

    cout << "ok" << endl;

    if(verbose)
    {
        cout << "Each UUID took an average of "  
	     << (double) ((finish - start).toMicroSeconds()) / howMany 
	     << " micro seconds to generate and insert into a set<string>." 
	     << endl;
    }

    return EXIT_SUCCESS;
}