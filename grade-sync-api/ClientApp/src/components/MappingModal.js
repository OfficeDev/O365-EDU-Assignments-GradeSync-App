// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
import React, {
    useContext,
    useEffect,
    useReducer,
    useState,
    Fragment } from 'react';
import { Modal, ModalHeader, ModalBody, ModalFooter } from 'reactstrap';
import ApiService from '../services/ApiService';
import { TeamsContext } from '../App';

import * as teamsjs from "@microsoft/teams-js";
const { authentication } = teamsjs;

const MappingModal = (props) => { 
    const { tabContext } = useContext(TeamsContext);

    const [canMoveNext, setCanMoveNext] = useState(false);
    const [canMovePrevious, setCanMovePrevious] = useState(false);
    const [cachedNextPage, setCachedNextPage] = useState(null);
    const [cachedPreviousPage, setCachedPreviousPage] = useState(null);

    const [showConfirmModal, setShowConfirmModal] = useState(false);
    const [accountsData, setAccountsData] = useState([]);
    const [activeAccountId, setActiveAccountId] = useState("none");
    const [classesData, setClassesData] = useState([]);
    const [activeClassId, setActiveClassId] = useState("none");

    const [fetchingAccount, setFetchingAccount] = useState(false);
    const [fetchingClass, setFetchingClass] = useState(false);
    const [fetchingStudents, setFetchingStudents] = useState(false);

    const [patching, setPatching] = useState(false);

    const pageHasData = (page) => {
        return props.dataState.mappingModalState.pageHasDataMap[page];
    }

    const graphStudentHasExternalId = (graphStudent) => {
        if (graphStudent.student && graphStudent.student.externalId && graphStudent.student.externalId !== "") return true;
        return false;
    }

    const initStudentState = {
        graphStudents: [],
        oneRosterStudents: [],
        graphUsedOneRosterMap: {},
        oneRosterIdMap: {},
        tempGraphStudentId: "",
        tempOneRosterStudentId: "",
        allStudentsHaveExternalId: false
    }

    const stateReducer = (state, action) => {
        const stateCopy = {...state};

        if (action.type === "setStudents") {
            stateCopy.graphStudents = action.students.graphStudents;
            stateCopy.oneRosterStudents = action.students.oneRosterStudents;

            for (const oneRosterStudent of stateCopy.oneRosterStudents) {
                stateCopy.oneRosterIdMap[oneRosterStudent.id] = oneRosterStudent;
            }

            for (const graphStudent of stateCopy.graphStudents) {
                if (graphStudentHasExternalId(graphStudent)) {
                    const id = graphStudent.student.externalId;

                    if (stateCopy.oneRosterIdMap.hasOwnProperty(id)) {
                        graphStudent.activeExternalId = id;
                        stateCopy.graphUsedOneRosterMap[id] = graphStudent;
                    } else {
                        graphStudent.activeExternalId = "not-from-class";
                    }
                } else {
                    graphStudent.activeExternalId = "none";
                }
            }
        }

        if (action.type === "changeTempStudentIds") {
            stateCopy.tempGraphStudentId = action.graphId;
            stateCopy.tempOneRosterStudentId = action.oneRosterId;
        }

        if (action.type === "changeActiveExternalId") {
            let allIds = true;
            for (const graphStudent of stateCopy.graphStudents) {
                if (graphStudent.userId === action.graphId) {
                    graphStudent.activeExternalId = action.oneRosterId;
                    stateCopy.graphUsedOneRosterMap[action.oneRosterId] = graphStudent;
                } else if (graphStudent.activeExternalId === "none") {
                    allIds = false;
                }
            }

            stateCopy.allStudentsHaveExternalId = allIds;
        }

        return stateCopy;
    }
    const [studentState, dispatch] = useReducer(stateReducer, initStudentState);

    const maxPageIdx = 2;
    const pageMetadata = {
        "accounts": {title: "Account linking", pageIdx: 0},
        "classes": {title: "Class linking", pageIdx: 1},
        "students": {title: "Student linking", pageIdx: 2}
    }

    const canMovePage = (increase) => {
        const currentPage = props.dataState.mappingModalState.activePage;
        const currentPageIdx = pageMetadata[currentPage].pageIdx;
        const currentPageHasData = pageHasData(currentPage);

        const changedVal = increase ? 1 : -1;
        const changeIdx = currentPageIdx + changedVal;

        // if changed page is out of index don't allow moving pages
        if (changeIdx < 0 || changeIdx > maxPageIdx) return false;

        let currIdx = 0;
        //when decreasing pages, store the possible pages we can decrease to in a stack, since we are iterating forwards
        const missingPageDataStack = []; 
        while (currIdx <= maxPageIdx) {
            const requestedPage = props.dataState.mappingModalState.pages[currIdx];
            const requestedPageHasData = pageHasData(requestedPage);

            if (increase) {
                if (currIdx >= changeIdx) {
                    // if user is an admin, we let them move to any pages
                    // if they are a teacher, they can only move to pages that don't have existing data, because they can't overwrite

                    // teachers also cannot move from the current page if they haven't created a data link yet
                    // this is because the pages are in order of which data you need before creating further links
                    if ((!requestedPageHasData && currentPageHasData) || props.isAdmin) {
                        setCachedNextPage(requestedPage);
                        return true;
                    }
                }
            } else {
                if (currIdx <= changeIdx) {
                    if ((!requestedPageHasData && currentPageHasData) || props.isAdmin) {
                        missingPageDataStack.push(requestedPage)
                    }
                }
            }

            currIdx++;
        }

        if (!increase && missingPageDataStack.length > 0) {
            const targetPage = missingPageDataStack.pop();
            setCachedPreviousPage(targetPage);
            return true;
        }
        return false;
    }

    useEffect(() => {
        if (tabContext && props.defaultConnectionId) {
            getInitialData();
            setCanMoveNext(canMovePage(true));
            setCanMovePrevious(canMovePage(false));
        }
    }, [tabContext, props.dataState, props.defaultConnectionId]);

    const getDataFromPage = async (page, token) => {
        const classId = tabContext["team"]["groupId"];

        switch (page) {
            case "accounts":
                setFetchingAccount(true);
                const accountsUrl = `/api/get-one-roster-teacher-matches/${props.defaultConnectionId}`;
                const accountsRes = await ApiService.apiGetRequest(token, accountsUrl);
                setAccountsData(accountsRes.data);

                if (pageHasData(page)) {
                    try {
                        const externalIdRes = await ApiService.apiGetRequest(token, "/api/get-teacher-external-id");
                        setActiveAccountId(externalIdRes.data);
                    } catch (e) {}
                }
                setFetchingAccount(false);
                break;
            case "classes":
                setFetchingClass(true);
                let classesUrl = `/api/get-one-roster-classes/${props.defaultConnectionId}`;
                if (activeAccountId !== "none") classesUrl = `${classesUrl}/${activeAccountId}`;
                const classRes = await ApiService.apiGetRequest(token, classesUrl);
                setClassesData(classRes.data);

                if (pageHasData(page)) {
                    try {
                        const classIdRes = await ApiService.apiGetRequest(token, `/api/get-class-external-id/${classId}`);
                        setActiveClassId(classIdRes.data);
                    } catch (e) {}
                }
                setFetchingClass(false);
                break;
            case "students":
                setFetchingStudents(true);
                let studentsUrl = `/api/get-teams-and-oneroster-students/${classId}/${props.defaultConnectionId}`;
                if (activeClassId !== "none") studentsUrl = `${studentsUrl}/${activeClassId}`;
                const studentsRes = await ApiService.apiGetRequest(token, studentsUrl);
                console.log(studentsRes.data);

                dispatch({ type: "setStudents", students: studentsRes.data });
                setFetchingStudents(false);
                break;
        }
    }

    const getInitialData = async () => {
        await teamsjs.app.initialize();
        const token = await authentication.getAuthToken();
        await getDataFromPage(props.dataState.mappingModalState.activePage, token);
    }

    const warningIcon = (active) => {
        const color = active ? "#FAC105" : "#FAE8AC";
        return (
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" fill={color} class="bi flex-shrink-0" viewBox="0 0 16 16" role="img" aria-label="Warning:">
                <path d="M8.982 1.566a1.13 1.13 0 0 0-1.96 0L.165 13.233c-.457.778.091 1.767.98 1.767h13.713c.889 0 1.438-.99.98-1.767L8.982 1.566zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 5.995A.905.905 0 0 1 8 5zm.002 6a1 1 0 1 1 0 2 1 1 0 0 1 0-2z"/>
            </svg>
        );
    }

    const successIcon = (active) => {
        const color = active ? "#1B8354" : "#A4D0B8";
        return (
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" fill={color} class="bi flex-shrink-0" viewBox="0 0 16 16" role="img" aria-label="Warning:">
                <path d="M16 8A8 8 0 1 1 0 8a8 8 0 0 1 16 0zm-3.97-3.03a.75.75 0 0 0-1.08.022L7.477 9.417 5.384 7.323a.75.75 0 0 0-1.06 1.06L6.97 11.03a.75.75 0 0 0 1.079-.02l3.992-4.99a.75.75 0 0 0-.01-1.05z"/>
            </svg>
        );
    }

    const getIcon = (success, active) => {
        if (success) return successIcon(active);
        return warningIcon(active);
    }

    const titleFromPage = (page) => {
        return pageMetadata[page]["title"];
    }

    const onAccountIdChange = (sourcedId) => {
        setActiveAccountId(sourcedId);
        setShowConfirmModal(true);
    }

    const onClassIdChange = (sourcedId) => {
        setActiveClassId(sourcedId)
        setShowConfirmModal(true);
    }

    const onStudentIdChange = (graphId, oneRosterId) => {
        dispatch({ type: "changeTempStudentIds", graphId: graphId, oneRosterId: oneRosterId});
        setShowConfirmModal(true);
    }

    const onStudentPatchSuccess = () => {
        dispatch({ type: "changeActiveExternalId", graphId: studentState.tempGraphStudentId, oneRosterId: studentState.tempOneRosterStudentId });
    }

    const progressBar = (mappingModalState) => {
        const accountActive = mappingModalState.activePage === "accounts" ? true : false;
        const classesActive = mappingModalState.activePage === "classes" ? true : false;
        const studentsActive = mappingModalState.activePage === "students" ? true : false;
        const allStudentsSuccess = mappingModalState.pageHasDataMap["students"] || studentState.allStudentsHaveExternalId ? true : false;

        return (
            <div className="d-flex flex-row justify-content-around flex-nowrap my-auto">
                <div className="d-flex flex-column" style={{ marginLeft: "8%"}}>
                    <div className="mx-auto mb-2">
                        { getIcon(mappingModalState.pageHasDataMap["accounts"], accountActive) }
                    </div>
                    <div className="mx-auto" style={{ color: accountActive ? "black" : "lightgray" }}>
                        Account
                    </div>
                </div>

                <hr style={{ flexGrow: 1 }} />

                <div className="d-flex flex-column">
                    <div className="mx-auto mb-2">
                        { getIcon(mappingModalState.pageHasDataMap["classes"], classesActive) }
                    </div>
                    <div className="mx-auto" style={{ color: classesActive ? "black" : "lightgray" }}>
                        Classes
                    </div>
                </div>

                <hr style={{ flexGrow: 1 }} />

                <div className="d-flex flex-column" style={{ marginRight: "8%"}}>
                    <div className="mx-auto mb-2">
                        { getIcon(allStudentsSuccess, studentsActive) }
                    </div>
                    <div className="mx-auto" style={{ color: studentsActive ? "black" : "lightgray" }}>
                        Students
                    </div>
                </div>
            </div>
        );
    }

    const oneRosterComboBox = (graphStudent) => {
        const teacherCanChange = graphStudent.activeExternalId === "none" ? true : false;

        if (!teacherCanChange && !props.isAdmin) {
            return (
                <select className="form-select" value={graphStudent.activeExternalId} disabled>
                    <option value="none" disabled>None</option>
                    <option value="not-from-class" disabled>Linked from other class</option>

                    {
                        studentState.oneRosterStudents.map((oneRosterStudent) => {
                            return (
                                <option value={oneRosterStudent.id}>{`${oneRosterStudent.givenName} ${oneRosterStudent.familyName} (${oneRosterStudent.username})`}</option>
                            );
                        })
                    }
                </select>
            ); 
        } else {
            return (
                <select 
                    className="form-select" 
                    value={graphStudent.activeExternalId}
                    onChange={e => onStudentIdChange(graphStudent.userId, e.target.value)}
                >
                    <option value="none" disabled>None</option>
                    <option value="not-from-class" disabled>Linked from other class</option>

                    {
                        studentState.oneRosterStudents.map((oneRosterStudent) => {
                            if (!props.isAdmin && studentState.graphUsedOneRosterMap.hasOwnProperty(oneRosterStudent.id)) {
                                // user is a teacher, and this externalId is already assigned, we no longer want to display it as a possible option
                                return null;
                            } else {
                                return (
                                    <option value={oneRosterStudent.id}>{`${oneRosterStudent.givenName} ${oneRosterStudent.familyName} (${oneRosterStudent.username})`}</option>
                                );
                            }
                        })
                    }
                </select>
            );
        }
    }

    const getStudentRow = (graphStudent, headerRow = false) => {
        const rowClass = headerRow ? "row mb-3" : "row mb-2";

        return (
            <div className={rowClass}>
                <div className="d-flex col-5 align-items-center">
                    {
                        headerRow ? <p className="fs-4 mb-2">Teams Student</p> :
                        <p className="my-auto">{graphStudent.displayName}</p>
                    }
                </div>
                <div className="d-flex col-2 align-items-center">
                    {
                        headerRow ? null :
                            <svg className="my-auto" width="24" height="20" xmlns="http://www.w3.org/2000/svg">
                                <path d="M21.883 12l-7.527 6.235.644.765 9-7.521-9-7.479-.645.764 7.529 6.236h-21.884v1h21.883z"/>
                            </svg>
                    }
                </div>
                <div className="d-flex col-5 align-items-center">
                    {
                        headerRow ? <p className="fs-4 mb-2">Gradebook Student</p> : oneRosterComboBox(graphStudent)
                    } 
                </div>
            </div>
        );
    }

    const getStudentLoadingRow = (spinner) => {
        return (
            <div className="row mb-2">
                <div className="d-flex col-5 align-items-center">{spinner}</div>
                <div className="d-flex col-2 align-items-center"></div>
                <div className="d-flex col-5 align-items-center">{spinner}</div>
            </div>
        );
    }

    const getBody = () => {
        const spinner = 
            <div className="spinner-grow spinner-grow-sm text-secondary">
                <span className="visually-hidden">Loading...</span>
            </div>
        let bodyJsx = null;
        let title = "";
        let desc = "";

        switch (props.dataState.mappingModalState.activePage) {
            case "accounts":
                title = "Step 1 - Link Account"
                desc = "Choose a Gradebook Teacher account to link to your Teams account. This action is permanent and can only be changed by an admin."

                bodyJsx = (
                    <Fragment>
                        <p className="fs-4 mb-2">Gradebook Teacher to link</p>
                        {
                            fetchingAccount ? spinner :
                            <select className="form-select" value={activeAccountId} style={{maxWidth: "400px"}} onChange={e => onAccountIdChange(e.target.value)}>
                                <option value="none" disabled>None</option>
                                {
                                    accountsData.map((account) => {
                                        return (
                                            <option value={account.id}>{`${account.givenName} ${account.familyName} (${account.username})`}</option>
                                        );
                                    })
                                }
                            </select>
                        }
                    </Fragment>
                );
                break;
            case "classes":
                title = "Step 2 - Link Class"
                desc = "Choose a Gradebook Class to link to this Teams Class. This action is permanent and can only be changed by an admin."

                bodyJsx = (
                    <Fragment>
                        <p className="fs-4 mb-2">Gradebook Class to link</p>
                        {
                            fetchingClass ? spinner :
                            <select className="form-select" value={activeClassId} style={{maxWidth: "400px"}} onChange={e => onClassIdChange(e.target.value)}>
                                <option value="none" disabled>None</option>
                                {
                                    classesData.map((oneRosterClass) => {
                                        return (
                                            <option value={oneRosterClass.id}>{`${oneRosterClass.title} (${oneRosterClass.classCode})`}</option>
                                        );
                                    })
                                }
                            </select>
                        }
                    </Fragment>
                );
                break;
            case "students":
                title = "Step 3 - Link Students"
                desc = "Choose a Gradebook Student to link to each student in your Teams Class. Linking a student is permanent and can only be changed by an admin."

                bodyJsx = (
                    <Fragment>
                        { getStudentRow(null, true) }

                        {
                            fetchingStudents ? getStudentLoadingRow(spinner) :
                            studentState.graphStudents.map((student) => {
                                return getStudentRow(student);
                            })
                        }
                    </Fragment>
                );
                break;
        }

        return(
            <div className="mt-3">
                <h5 className="mb-2">{title}</h5>
                <p className="mb-5">{desc}</p>

                { bodyJsx }
            </div>
        );
    }

    const onActionConfirm = async () => {
        setPatching(true);
        const token = await authentication.getAuthToken();
        const classId = tabContext["team"]["groupId"];

        switch (props.dataState.mappingModalState.activePage) {
            case "accounts":
                await ApiService.apiGetRequest(token, `/api/patch-teacher-external-id/${activeAccountId}/${props.defaultConnectionId}`);
                setShowConfirmModal(false);
                props.markPageComplete("accounts");
                break;
            case "classes":
                await ApiService.apiGetRequest(token, `/api/patch-class-external-id/${classId}/${activeClassId}/${props.defaultConnectionId}`);
                setShowConfirmModal(false);
                props.markPageComplete("classes");
                break;
            case "students":
                const path = `/api/patch-student-external-id/${classId}/${studentState.tempGraphStudentId}/${studentState.tempOneRosterStudentId}/${props.defaultConnectionId}`;
                try {
                    await ApiService.apiGetRequest(token, path);
                    onStudentPatchSuccess();
                    setShowConfirmModal(false);
                } catch (e) {}
                break;
        }

        setPatching(false);
    }

    const onActionCancel = () => {
        switch (props.dataState.mappingModalState.activePage) {
            case "accounts":
                setActiveAccountId("none");
                break;
            case "classes":
                setActiveClassId("none");
                break;
        }

        setShowConfirmModal(false);
    }

    const getConfirmModalBodyContent = () => {
        switch (props.dataState.mappingModalState.activePage) {
            case "accounts":
                const account = accountsData.find(account => account.id === activeAccountId);
                if (!account) return null;
                
                return (
                    <p>
                        Are you sure you want to link your Teams account to this Gradebook Teacher account? This action is permanent.
                        <br/>
                        <br/>
                        <strong>{`${account.givenName} ${account.familyName} (${account.username})`}</strong>
                    </p>
                );
            case "classes":
                const oneRosterClass = classesData.find(entity => entity.id === activeClassId);
                if (!oneRosterClass) return null;
                
                return (
                    <p>
                        Are you sure you want to link the Teams Class to this Gradebook Class? This action is permanent.
                        <br/>
                        <br/>
                        <strong>{`${oneRosterClass.title} (${oneRosterClass.classCode})`}</strong>
                    </p>
                );
            case "students":
                const graphStudent = studentState.graphStudents.find(entity => entity.userId === studentState.tempGraphStudentId);
                const oneRosterStudent = studentState.oneRosterIdMap[studentState.tempOneRosterStudentId];
                if (!graphStudent || !oneRosterStudent) return null;

                return (
                    <p>
                        Are you sure you want to link the Teams Student <strong>{graphStudent.displayName}</strong> to the following Gradebook Student? This action is permanent.
                        <br/>
                        <br/>
                        <strong>{`${oneRosterStudent.givenName} ${oneRosterStudent.familyName} (${oneRosterStudent.username})`}</strong>
                    </p>
                );
        }
    }

    const patchingSpinner = () => {
        if (!patching) return null;
        return (
            <span className="float-end my-auto me-3">
                <div className="spinner-border text-primary">
                    <span className="visually-hidden">Syncing</span>
                </div>
            </span>
        );
    }

    const backButton = () => {
        if (canMovePrevious) {
            return (
                <button 
                    type="button" 
                    className="btn btn-primary"
                    onClick={() => props.changePage(cachedPreviousPage)}
                >Back 
                </button>
            );
        } else {
            return (
                <button 
                    type="button" 
                    className="btn btn-primary"
                    disabled
                >Back 
                </button>
            );
        }
    }

    const nextButton = () => {
        if (canMoveNext) {
            return (
                <button
                    type="button"
                    className="btn btn-primary"
                    onClick={() => props.changePage(cachedNextPage)}
                >Next
                </button>
            );
        } else {
            return (
                <button
                    type="button"
                    className="btn btn-primary"
                    disabled
                >Next
                </button>
            );
        }
    }

    const getConfirmModal = () => {
        return (
            <Modal isOpen={showConfirmModal} size="sm" style={{ maxWidth: "900px", width: "40%"}}>
                <ModalHeader>
                    Confirm link
                </ModalHeader>

                <ModalBody>
                    { getConfirmModalBodyContent() }
                </ModalBody>

                <ModalFooter>
                    { patchingSpinner() }
                    <button type="button" className="btn btn-danger" onClick={onActionCancel}>Cancel</button>
                    <button type="button" className="btn btn-primary" onClick={onActionConfirm}>Confirm</button>
                </ModalFooter>
            </Modal>
        );
    }

    return (
        <Modal 
            isOpen={props.isOpen} 
            toggle={props.toggle}
            onClosed={props.onModalClose}
            scrollable={true} 
            size='lg' 
            style={{ maxWidth: "1600px", width: "70%", maxHeight: "700px"}}
            contentClassName="mapping-modal-height"
        >
            <ModalHeader toggle={props.toggle}>{ titleFromPage(props.dataState.mappingModalState.activePage) }</ModalHeader>

            <ModalBody style={{ backgroundColor: "#F5F5F5", minHeight: "100px", maxHeight: "100px" }}>
                { progressBar(props.dataState.mappingModalState) }
            </ModalBody>

            <ModalBody>
                { getBody() }
            </ModalBody>

            <ModalFooter>
                { backButton() }
                { nextButton() }
            </ModalFooter>

            { getConfirmModal() }
        </Modal>
    );
}

export default MappingModal;
