// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
import React, { useEffect, createContext, useState } from 'react';
import { Route, Routes } from 'react-router-dom';
import Home from "./components/Home";
import Connections from './components/Connections';
import ApiService from './services/ApiService';
import './custom.css';

import * as teamsjs from "@microsoft/teams-js";
const { authentication } = teamsjs;

export const TeamsContext = createContext(null);

const App = () => {
    const [tabContext, setTabContext] = useState(null);
    const [eduPrimaryRole, setEduPrimaryRole] = useState(null);
    const [isAdmin, setIsAdmin] = useState(false);

    const initializeTeams = async () => {
        try {
            await teamsjs.app.initialize();
            const token = await authentication.getAuthToken();
            const res = await ApiService.apiGetRequest(token, "/api/is-student-role");
            setEduPrimaryRole(res.data ? "student": "non-student");

            const context = await teamsjs.app.getContext();
            setTabContext(context);
        } catch (e) {
            console.log(e);
        }
    }

    useEffect(() => {
        initializeTeams();
    }, []);

    return (
        <TeamsContext.Provider value={{ tabContext: tabContext }}>
            <div className="container-fluid mt-3 px-0 overflow-hidden">
                <Routes>
                    <Route path="/" element={<Home eduPrimaryRole={eduPrimaryRole} isAdmin={isAdmin} setIsAdmin={setIsAdmin} />} />
                    <Route path="/manage-connections" element={<Connections isAdmin={isAdmin} />} />
                </Routes>
            </div>
        </TeamsContext.Provider>
    );
}

export default App;
